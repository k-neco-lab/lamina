// Copyright (c) K Lab. Licensed under the MIT License.

using System.Numerics;

namespace KLab.Lamina.Ui;

// Base for controls that own a list of children.
public abstract class ContainerControl : Control
{
    private readonly List<Control> children = new();

    public override IEnumerable<Control> Children => children;

    public int ChildCount => children.Count;

    public void Add(Control child)
    {
        children.Add(child);
        child.Attach(this, Root);
        InvalidateLayout();
    }

    public void Remove(Control child)
    {
        if (children.Remove(child))
        {
            child.Attach(null, null);
            InvalidateLayout();
        }
    }

    public void Clear()
    {
        foreach (var child in children)
        {
            child.Attach(null, null);
        }
        children.Clear();
        InvalidateLayout();
    }

    protected IReadOnlyList<Control> Items => children;

    // Child changes made while arranging (virtualization) must not request another layout pass.
    protected void Adopt(Control child)
    {
        children.Add(child);
        child.Attach(this, Root);
    }

    protected void Release(Control child)
    {
        children.Remove(child);
        child.Attach(null, null);
    }

    protected void RenderChildren(IDrawContext context)
    {
        foreach (var child in children)
        {
            child.Render(context);
        }
    }

    protected override void RenderContent(IDrawContext context, Rect area) => RenderChildren(context);
}
