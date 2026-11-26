#!/usr/bin/env python3
"""
Publish Lamina .nupkg packages into a static NuGet V3 feed on an orphan Git branch (`feed`).

Zero GitHub Actions, zero external services: packs are built locally (e.g. via `dotnet pack`)
and published directly as static JSON + .nupkg files served via raw.githubusercontent.com.

Usage:
    # Build static feed directory only (dry run / inspection):
    python3 scripts/publish-feed.py --nupkgs artifacts/nupkgs --out artifacts/feed

    # Update local orphan `feed` branch and push to origin/feed:
    python3 scripts/publish-feed.py --nupkgs artifacts/nupkgs --branch feed --push
"""

from __future__ import annotations

import argparse
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET
import zipfile
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any

DEFAULT_BASE_URL = "https://raw.githubusercontent.com/k-neco-lab/lamina/feed"


@dataclass
class Dependency:
    id: str
    range: str


@dataclass
class DependencyGroup:
    target_framework: str | None
    dependencies: list[Dependency] = field(default_factory=list)


@dataclass
class PackageMeta:
    id: str
    version: str
    authors: str
    description: str
    license_expression: str
    project_url: str
    tags: list[str]
    dependency_groups: list[DependencyGroup]
    nuspec_bytes: bytes
    nupkg_path: Path

    @property
    def id_lower(self) -> str:
        return self.id.lower()

    @property
    def version_lower(self) -> str:
        return self.version.lower()


def semver_sort_key(version: str) -> tuple[Any, ...]:
    """Sort SemVer 2.0 versions (e.g. 0.1.0-alpha.1 < 0.1.0 < 0.2.0)."""
    core, _, rest = version.partition("+")
    core, has_pre, pre = core.partition("-")
    nums = tuple(int(p) if p.isdigit() else 0 for p in core.split("."))
    while len(nums) < 4:
        nums = nums + (0,)
    if not has_pre:
        return (nums, 1, ())
    pre_parts = tuple((0, int(p)) if p.isdigit() else (1, p.lower()) for p in pre.split("."))
    return (nums, 0, pre_parts)


def normalize_version_range(ver: str) -> str:
    ver = ver.strip()
    if not ver:
        return "(, )"
    if ver.startswith("[") or ver.startswith("("):
        return ver
    return f"[{ver}, )"


def parse_nupkg(nupkg_path: Path) -> PackageMeta:
    with zipfile.ZipFile(nupkg_path, "r") as zf:
        nuspec_names = [n for n in zf.namelist() if n.endswith(".nuspec") and "/" not in n]
        if not nuspec_names:
            raise ValueError(f"No root .nuspec found in {nupkg_path}")
        nuspec_bytes = zf.read(nuspec_names[0])

    root = ET.fromstring(nuspec_bytes)
    ns_match = re.match(r"\{(.+)\}", root.tag)
    ns = {"n": ns_match.group(1)} if ns_match else {}

    def find_text(parent: ET.Element, tag: str) -> str:
        el = parent.find(f"n:{tag}", ns) if ns else parent.find(tag)
        return (el.text or "").strip() if el is not None else ""

    meta_el = root.find("n:metadata", ns) if ns else root.find("metadata")
    if meta_el is None:
        raise ValueError(f"No <metadata> element in {nupkg_path}")

    pkg_id = find_text(meta_el, "id")
    version = find_text(meta_el, "version")
    authors = find_text(meta_el, "authors")
    description = find_text(meta_el, "description")
    license_el = meta_el.find("n:license", ns) if ns else meta_el.find("license")
    license_expr = ""
    if license_el is not None and license_el.attrib.get("type") == "expression":
        license_expr = (license_el.text or "").strip()
    project_url = find_text(meta_el, "projectUrl")
    tags_raw = find_text(meta_el, "tags")
    tags = [t for t in re.split(r"[\s,;]+", tags_raw) if t]

    dep_groups: list[DependencyGroup] = []
    deps_el = meta_el.find("n:dependencies", ns) if ns else meta_el.find("dependencies")
    if deps_el is not None:
        groups = deps_el.findall("n:group", ns) if ns else deps_el.findall("group")
        if groups:
            for g in groups:
                tfm = g.attrib.get("targetFramework")
                deps: list[Dependency] = []
                for d in g.findall("n:dependency", ns) if ns else g.findall("dependency"):
                    deps.append(
                        Dependency(
                            id=d.attrib["id"],
                            range=normalize_version_range(d.attrib.get("version", "")),
                        )
                    )
                dep_groups.append(DependencyGroup(target_framework=tfm, dependencies=deps))
        else:
            deps = []
            for d in deps_el.findall("n:dependency", ns) if ns else deps_el.findall("dependency"):
                deps.append(
                    Dependency(
                        id=d.attrib["id"],
                        range=normalize_version_range(d.attrib.get("version", "")),
                    )
                )
            if deps:
                dep_groups.append(DependencyGroup(target_framework=None, dependencies=deps))

    return PackageMeta(
        id=pkg_id,
        version=version,
        authors=authors,
        description=description,
        license_expression=license_expr,
        project_url=project_url,
        tags=tags,
        dependency_groups=dep_groups,
        nuspec_bytes=nuspec_bytes,
        nupkg_path=nupkg_path,
    )


def write_json(path: Path, data: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(data, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


def build_static_feed(nupkg_paths: list[Path], out_dir: Path, base_url: str) -> list[PackageMeta]:
    base_url = base_url.rstrip("/")
    flat_base = f"{base_url}/v3-flatcontainer/"
    reg_base = f"{base_url}/v3-registration/"

    # Merge any existing .nupkg files already in out_dir/v3-flatcontainer with new ones.
    # New packages overwrite matching (id_lower, version_lower).
    packages_by_key: dict[tuple[str, str], PackageMeta] = {}

    existing_flat = out_dir / "v3-flatcontainer"
    if existing_flat.exists():
        for existing_nupkg in sorted(existing_flat.rglob("*.nupkg")):
            meta = parse_nupkg(existing_nupkg)
            packages_by_key[(meta.id_lower, meta.version_lower)] = meta

    for nupkg in sorted(nupkg_paths):
        meta = parse_nupkg(nupkg)
        packages_by_key[(meta.id_lower, meta.version_lower)] = meta

    if not packages_by_key:
        raise RuntimeError("No .nupkg files found to publish.")

    # Group by lower-case package ID
    by_id: dict[str, list[PackageMeta]] = {}
    for meta in packages_by_key.values():
        by_id.setdefault(meta.id_lower, []).append(meta)

    for id_lower in by_id:
        by_id[id_lower].sort(key=lambda m: semver_sort_key(m.version))

    # Write flat container + registration files into a staging directory, then swap cleanly.
    out_dir.mkdir(parents=True, exist_ok=True)
    flat_root = out_dir / "v3-flatcontainer"
    reg_root = out_dir / "v3-registration"

    # 1. Service index (index.json)
    service_index = {
        "version": "3.0.0",
        "resources": [
            {
                "@id": flat_base,
                "@type": "PackageBaseAddress/3.0.0",
                "comment": "Base URL of where NuGet packages are stored (flat container).",
            },
            {
                "@id": reg_base,
                "@type": "RegistrationsBaseUrl/3.6.0",
                "comment": "Base URL of static NuGet package registrations.",
            },
            {
                "@id": reg_base,
                "@type": "RegistrationsBaseUrl/3.0.0-beta",
                "comment": "Base URL of static NuGet package registrations.",
            },
            {
                "@id": reg_base,
                "@type": "RegistrationsBaseUrl",
                "comment": "Base URL of static NuGet package registrations.",
            },
        ],
    }
    write_json(out_dir / "index.json", service_index)

    # 2. Per-package flat container & registration metadata
    for id_lower, versions in sorted(by_id.items()):
        # Flat container version list
        write_json(
            flat_root / id_lower / "index.json",
            {"versions": [m.version_lower for m in versions]},
        )

        reg_items: list[dict[str, Any]] = []
        reg_index_url = f"{reg_base}{id_lower}/index.json"

        for meta in versions:
            ver_dir = flat_root / id_lower / meta.version_lower
            ver_dir.mkdir(parents=True, exist_ok=True)

            target_nupkg = ver_dir / f"{id_lower}.{meta.version_lower}.nupkg"
            if meta.nupkg_path.resolve() != target_nupkg.resolve():
                shutil.copy2(meta.nupkg_path, target_nupkg)

            target_nuspec = ver_dir / f"{id_lower}.nuspec"
            target_nuspec.write_bytes(meta.nuspec_bytes)

            pkg_content_url = f"{flat_base}{id_lower}/{meta.version_lower}/{id_lower}.{meta.version_lower}.nupkg"
            leaf_url = f"{reg_base}{id_lower}/{meta.version_lower}.json"

            # Registration leaf
            write_json(
                reg_root / id_lower / f"{meta.version_lower}.json",
                {
                    "@id": leaf_url,
                    "listed": True,
                    "packageContent": pkg_content_url,
                    "registration": reg_index_url,
                },
            )

            dep_groups_json: list[dict[str, Any]] = []
            for g in meta.dependency_groups:
                entry: dict[str, Any] = {}
                if g.target_framework:
                    entry["targetFramework"] = g.target_framework
                entry["dependencies"] = [
                    {"id": d.id, "range": d.range} for d in g.dependencies
                ]
                dep_groups_json.append(entry)

            catalog_entry: dict[str, Any] = {
                "@id": leaf_url,
                "id": meta.id,
                "version": meta.version,
                "authors": meta.authors,
                "description": meta.description,
                "listed": True,
                "packageContent": pkg_content_url,
                "tags": meta.tags,
                "dependencyGroups": dep_groups_json,
            }
            if meta.license_expression:
                catalog_entry["licenseExpression"] = meta.license_expression
            if meta.project_url:
                catalog_entry["projectUrl"] = meta.project_url

            reg_items.append(
                {
                    "@id": leaf_url,
                    "packageContent": pkg_content_url,
                    "registration": reg_index_url,
                    "catalogEntry": catalog_entry,
                }
            )

        lower_ver = versions[0].version_lower
        upper_ver = versions[-1].version_lower
        reg_index = {
            "@id": reg_index_url,
            "count": 1,
            "items": [
                {
                    "@id": f"{reg_index_url}#page/{lower_ver}/{upper_ver}",
                    "count": len(reg_items),
                    "lower": lower_ver,
                    "upper": upper_ver,
                    "items": reg_items,
                }
            ],
        }
        write_json(reg_root / id_lower / "index.json", reg_index)

    # Write a small README on the feed branch so browsing it on GitHub is clear
    readme_lines = [
        "# Lamina Static NuGet V3 Feed",
        "",
        "Static NuGet V3 package feed for **K Lab**'s Lamina packages, published locally via `scripts/publish-feed.py` (zero GitHub Actions).",
        "",
        "## Feed URL",
        "",
        f"`{base_url}/index.json`",
        "",
        "## Published Packages",
        "",
        "| Package | Versions |",
        "| --- | --- |",
    ]
    for id_lower, versions in sorted(by_id.items()):
        display_id = versions[-1].id
        ver_list = ", ".join(f"`{v.version}`" for v in versions)
        readme_lines.append(f"| `{display_id}` | {ver_list} |")
    readme_lines.append("")
    (out_dir / "README.md").write_text("\n".join(readme_lines), encoding="utf-8")

    return [m for vs in by_id.values() for m in vs]


def run_git(repo_dir: Path, args: list[str], check: bool = True) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        ["git", *args],
        cwd=repo_dir,
        text=True,
        capture_output=True,
        check=check,
    )


def publish_to_git_branch(
    repo_dir: Path,
    nupkg_paths: list[Path],
    branch: str,
    remote: str,
    base_url: str,
    push: bool,
    commit_message: str | None,
) -> None:
    # Check if branch exists locally or on remote
    local_exists = run_git(repo_dir, ["rev-parse", "--verify", f"refs/heads/{branch}"], check=False).returncode == 0
    remote_exists = run_git(repo_dir, ["rev-parse", "--verify", f"refs/remotes/{remote}/{branch}"], check=False).returncode == 0

    with tempfile.TemporaryDirectory(prefix="lamina-feed-wt-") as tmp:
        wt_dir = Path(tmp) / "feed"
        try:
            if local_exists:
                run_git(repo_dir, ["worktree", "add", str(wt_dir), branch])
            elif remote_exists:
                run_git(repo_dir, ["worktree", "add", "-b", branch, str(wt_dir), f"{remote}/{branch}"])
            else:
                run_git(repo_dir, ["worktree", "add", "--orphan", "-b", branch, str(wt_dir)])

            published = build_static_feed(nupkg_paths, wt_dir, base_url)
            run_git(wt_dir, ["add", "-A"])

            status = run_git(wt_dir, ["status", "--porcelain"]).stdout.strip()
            if not status:
                print(f"No changes to commit on branch '{branch}'.")
            else:
                if not commit_message:
                    summary = ", ".join(f"{m.id}@{m.version}" for m in sorted(published, key=lambda x: (x.id_lower, x.version_lower)))
                    commit_message = f"chore(feed): publish {summary}"
                run_git(wt_dir, ["commit", "-m", commit_message])
                print(f"Committed static NuGet V3 feed to branch '{branch}': {commit_message}")

            if push:
                run_git(wt_dir, ["push", remote, branch])
                print(f"Pushed branch '{branch}' to '{remote}'.")
        finally:
            run_git(repo_dir, ["worktree", "remove", "--force", str(wt_dir)], check=False)


def main() -> int:
    parser = argparse.ArgumentParser(description="Build and publish Lamina static NuGet V3 feed.")
    parser.add_argument(
        "--nupkgs",
        type=Path,
        default=Path("artifacts/nupkgs"),
        help="Directory containing .nupkg files (default: artifacts/nupkgs)",
    )
    parser.add_argument(
        "--base-url",
        default=DEFAULT_BASE_URL,
        help=f"Public base URL of the static feed (default: {DEFAULT_BASE_URL})",
    )
    parser.add_argument(
        "--out",
        type=Path,
        default=None,
        help="Output directory for dry-run / local static feed generation (skips git branch operations).",
    )
    parser.add_argument(
        "--branch",
        default="feed",
        help="Orphan git branch name to publish into (default: feed).",
    )
    parser.add_argument(
        "--remote",
        default="origin",
        help="Git remote name (default: origin).",
    )
    parser.add_argument(
        "--push",
        action="store_true",
        help="Push the updated feed branch to remote.",
    )
    parser.add_argument(
        "--message",
        default=None,
        help="Optional custom git commit message.",
    )
    args = parser.parse_args()

    nupkg_dir: Path = args.nupkgs.resolve()
    if not nupkg_dir.is_dir():
        print(f"Error: nupkgs directory not found: {nupkg_dir}", file=sys.stderr)
        return 1

    nupkg_paths = sorted(p for p in nupkg_dir.glob("*.nupkg") if not p.name.endswith(".symbols.nupkg"))
    if not nupkg_paths:
        print(f"Error: no .nupkg files found in {nupkg_dir}", file=sys.stderr)
        return 1

    if args.out is not None:
        published = build_static_feed(nupkg_paths, args.out.resolve(), args.base_url)
        print(f"Generated static NuGet V3 feed in {args.out.resolve()} ({len(published)} package version(s)).")
        return 0

    repo_dir = Path(__file__).resolve().parent.parent
    publish_to_git_branch(
        repo_dir=repo_dir,
        nupkg_paths=nupkg_paths,
        branch=args.branch,
        remote=args.remote,
        base_url=args.base_url,
        push=args.push,
        commit_message=args.message,
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
