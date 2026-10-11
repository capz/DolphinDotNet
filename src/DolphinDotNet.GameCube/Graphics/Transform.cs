using System;
using Dolphin.Collections;

namespace Dolphin.Graphics;

/// <summary>Cached TRS transform. Parenting preserves local coordinates; Euler rotation uses local XYZ degrees.</summary>
public sealed class Transform
{
    private const int MaximumHierarchyDepth = 128;
    // Native TRS layout: position XYZ, quaternion XYZW, scale XYZ.
    private readonly float[] values = new float[10];
    private readonly float[] local = new float[16];
    private readonly float[] world = new float[16];
    private readonly List<Transform> children = new List<Transform>();
    private Transform? parent;
    private bool localDirty = true;
    private bool worldDirty = true;

    public Transform()
    {
        values[6] = 1;
        values[7] = values[8] = values[9] = 1;
    }

    public Vector3 LocalPosition
    {
        get => new Vector3(values[0], values[1], values[2]);
        set
        {
            if (values[0] == value.X && values[1] == value.Y && values[2] == value.Z) return;
            values[0] = value.X;
            values[1] = value.Y;
            values[2] = value.Z;
            Changed();
        }
    }

    public Quaternion LocalRotation
    {
        get => new Quaternion(values[3], values[4], values[5], values[6]);
        set
        {
            if (values[3] == value.X && values[4] == value.Y && values[5] == value.Z && values[6] == value.W) return;
            values[3] = value.X;
            values[4] = value.Y;
            values[5] = value.Z;
            values[6] = value.W;
            Changed();
        }
    }

    public Vector3 LocalScale
    {
        get => new Vector3(values[7], values[8], values[9]);
        set
        {
            if (values[7] == value.X && values[8] == value.Y && values[9] == value.Z) return;
            values[7] = value.X;
            values[8] = value.Y;
            values[9] = value.Z;
            Changed();
        }
    }

    public Vector3 Position
    {
        get
        {
            var matrix = WorldMatrix;
            return new Vector3(matrix[12], matrix[13], matrix[14]);
        }
        set
        {
            if (parent == null) LocalPosition = value;
            else
            {
                if (NativeModel.ModelPosition(values, parent.WorldMatrix, value.X, value.Y, value.Z) < 0)
                    throw new ArgumentException("Invalid world position or parent transform.");
                Changed();
            }
        }
    }

    public Transform? Parent
    {
        get => parent;
        set
        {
            if (value == parent) return;
            int depth = 1;
            for (var ancestor = value; ancestor != null; ancestor = ancestor.parent)
            {
                if (ancestor == this) throw new ArgumentException("Transform cycle.");
                depth++;
                CheckDepth(depth);
            }
            // Validate the complete subtree before changing either parent's child list.
            ValidateDepth(depth);
            if (value != null) value.children.Add(this);
            if (parent != null) parent.children.Remove(this);
            parent = value;
            DirtyWorld();
        }
    }

    private static void CheckDepth(int depth)
    {
        if (depth > MaximumHierarchyDepth)
            throw new ArgumentException("Transform hierarchy exceeds 128 levels.");
    }

    private void ValidateDepth(int depth)
    {
        CheckDepth(depth);
        for (int i = 0; i < children.Count; i++) children[i].ValidateDepth(depth + 1);
    }

    private void Changed()
    {
        localDirty = true;
        DirtyWorld();
    }

    private void DirtyWorld()
    {
        // A dirty node's descendants are already dirty. Stop repeated subtree walks.
        if (worldDirty) return;
        worldDirty = true;
        for (int i = 0; i < children.Count; i++) children[i].DirtyWorld();
    }

    public void Translate(Vector3 offset, bool worldSpace = false)
    {
        if (worldSpace) Position = Position + offset;
        else
        {
            if (NativeModel.ModelTranslate(values, offset.X, offset.Y, offset.Z) < 0)
                throw new ArgumentException("Invalid translation.");
            Changed();
        }
    }

    public void Rotate(float xDegrees, float yDegrees, float zDegrees)
    {
        if (NativeModel.ModelRotate(values, xDegrees, yDegrees, zDegrees) < 0)
            throw new ArgumentException("Invalid rotation.");
        Changed();
    }

    internal float[] WorldMatrix
    {
        get
        {
            if (!worldDirty) return world;
            if (localDirty)
            {
                if (NativeModel.ModelTrs(values, local) < 0)
                    throw new ArgumentException("Transform must be finite, with nonzero quaternion and scale.");
                localDirty = false;
            }
            if (parent == null)
            {
                for (int i = 0; i < 16; i++) world[i] = local[i];
            }
            else if (NativeModel.ModelMultiply(parent.WorldMatrix, local, world) < 0)
                throw new ArgumentException("World transform is invalid.");
            worldDirty = false;
            return world;
        }
    }

    /// <summary>Copies to a caller-owned column-major 16-float buffer; never exposes the cached array.</summary>
    public void CopyWorldMatrix(float[] destination)
    {
        if (destination == null || destination.Length != 16)
            throw new ArgumentException("Expected 16-float matrix.");
        var matrix = WorldMatrix;
        for (int i = 0; i < 16; i++) destination[i] = matrix[i];
    }
}
