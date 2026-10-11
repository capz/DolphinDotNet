using System;
using Dolphin.Graphics;

static class TransformChecks
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public static void Run()
    {
        var matrix = new float[16];
        var root = new Transform();
        root.LocalPosition = new Vector3(3, 0, 0);
        root.LocalScale = new Vector3(2, 3, 4);
        root.Rotate(0, 0, 90);
        var child = new Transform();
        child.Parent = root;
        child.LocalPosition = new Vector3(1, 0, 0);
        var leaf = new Transform();
        leaf.Parent = child;
        leaf.LocalPosition = new Vector3(1, 0, 0);
        leaf.CopyWorldMatrix(matrix);
        Check(matrix[12] > 2.999f && matrix[12] < 3.001f && matrix[13] > 3.999f && matrix[13] < 4.001f,
            "Rotated and scaled ancestor placement.");

        root.LocalPosition = new Vector3(5, 0, 0);
        // The leaf is clean here, so ancestor invalidation must reach both generations.
        leaf.CopyWorldMatrix(matrix);
        Check(matrix[12] > 4.999f && matrix[12] < 5.001f, "Grandchild cache invalidation.");
        child.Position = new Vector3(5, 6, 0);
        Check(child.LocalPosition.X > 2.999f && child.LocalPosition.X < 3.001f,
            "World positioning through scaled and rotated parent.");

        child.Parent = null;
        leaf.CopyWorldMatrix(matrix);
        Check(matrix[12] > 3.999f && matrix[12] < 4.001f, "Detaching preserves local placement.");
        root.LocalPosition = new Vector3(100, 0, 0);
        leaf.CopyWorldMatrix(matrix);
        Check(matrix[12] > 3.999f && matrix[12] < 4.001f, "Former parent no longer affects subtree.");
        matrix[12] = 999;
        leaf.CopyWorldMatrix(matrix);
        Check(matrix[12] != 999, "Caller cannot mutate the cached matrix.");

        try { child.Parent = leaf; throw new InvalidOperationException("Cycle accepted."); }
        catch (ArgumentException) { }
        Check(child.Parent == null && leaf.Parent == child, "Rejected cycle preserves links.");

        var hierarchyRoot = new Transform();
        var deepest = hierarchyRoot;
        for (int depth = 1; depth < 128; depth++)
        {
            var next = new Transform();
            next.Parent = deepest;
            deepest = next;
        }
        deepest.CopyWorldMatrix(matrix);
        Check(matrix[0] == 1 && matrix[15] == 1, "Maximum-depth hierarchy works.");
        var rejected = new Transform();
        try { rejected.Parent = deepest; throw new InvalidOperationException("Excess depth accepted."); }
        catch (ArgumentException) { }
        Check(rejected.Parent == null, "Rejected depth preserves parent.");
        try { child.Parent = deepest; throw new InvalidOperationException("Excess subtree depth accepted."); }
        catch (ArgumentException) { }
        Check(child.Parent == null && leaf.Parent == child, "Rejected subtree move preserves links.");

        root.LocalRotation = new Quaternion(0, 0, 0, 0);
        try { root.CopyWorldMatrix(matrix); throw new InvalidOperationException("Zero quaternion accepted."); }
        catch (ArgumentException) { }
        root.LocalRotation = Quaternion.Identity;
        root.CopyWorldMatrix(matrix);
        Check(matrix[0] == 2, "Invalid transform can be corrected and recalculated.");

        var empty = new GameObject();
        Check(empty.Model == null, "Empty object owns only a transform.");
        empty.Update(0.1f);
        var renderer = new Renderer();
        renderer.Draw(empty);
        empty.Dispose();
        empty.Dispose();
        try { empty.Update(0); throw new InvalidOperationException("Disposed object update accepted."); }
        catch (ObjectDisposedException) { }
        try { renderer.Draw(empty); throw new InvalidOperationException("Disposed object draw accepted."); }
        catch (ObjectDisposedException) { }
    }
}
