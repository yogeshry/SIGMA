using UnityEngine;

/// <summary>
/// Provides spatial data (pose, surface extents, axes) for devices tracked in Unity.
/// </summary>
public static class DeviceSpatialProvider
{
    /// <summary>Position and orientation of the device in world space.</summary>
    public readonly struct DevicePose
    {
        public readonly Vector3 Position;
        public readonly Quaternion Rotation;

        public DevicePose(Vector3 position, Quaternion rotation)
        {
            Position = position;
            Rotation = rotation;
        }
    }

    /// <summary>Represents an edge defined by two corner points.</summary>
    public readonly struct Edge
    {
        public readonly Vector3 CornerA;
        public readonly Vector3 CornerB;

        public Edge(Vector3 cornerA, Vector3 cornerB)
        {
            CornerA = cornerA;
            CornerB = cornerB;
        }
    }

    /// <summary>Grouping of all four device edges.</summary>
    public readonly struct Edges
    {
        public readonly Edge Top;
        public readonly Edge Left;
        public readonly Edge Bottom;
        public readonly Edge Right;

        public Edges(Edge top, Edge left, Edge bottom, Edge right)
        {
            Top = top;
            Left = left;
            Bottom = bottom;
            Right = right;
        }
    }

    /// <summary>Corner points of the device surface.</summary>
    public readonly struct Corners
    {
        public readonly Vector3 TopRight;
        public readonly Vector3 TopLeft;
        public readonly Vector3 BottomRight;
        public readonly Vector3 BottomLeft;

        public Corners(Vector3 topRight, Vector3 topLeft, Vector3 bottomRight, Vector3 bottomLeft)
        {
            TopRight = topRight;
            TopLeft = topLeft;
            BottomRight = bottomRight;
            BottomLeft = bottomLeft;
        }
    }

    /// <summary>Corners and edges of the device's surface.</summary>
    public readonly struct SurfaceExtent
    {
        public readonly Corners Corners;
        public readonly Edges Edges;

        public SurfaceExtent(Corners corners, Edges edges)
        {
            Corners = corners;
            Edges = edges;
        }
    }

    /// <summary>Local axis directions of the device.</summary>
    public readonly struct AxisDirections
    {
        public readonly Vector3 Up;
        public readonly Vector3 Forward;
        public readonly Vector3 Right;

        public AxisDirections(Vector3 up, Vector3 forward, Vector3 right)
        {
            Up = up;
            Forward = forward;
            Right = right;
        }
    }

    /// <summary>
    /// Returns the world-space pose of the device.
    /// </summary>
    public static DevicePose GetDevicePose(Device info)
    {
        Transform t = info.getTransform();
        return new DevicePose(t.position, t.rotation);
    }

    /// <summary>
    /// Computes the corners and edges (as corner pairs) of the device's surface.
    /// </summary>
    /// <param name="info">DeviceInfo with a valid Transform.</param>
    /// <param name="physicalSize">Physical size (width, height) in world units.</param>
    public static SurfaceExtent GetSurfaceExtent(Device info, Vector2 physicalSize)
    {
        Transform t = info.getTransform();
        Vector3 center = t.position;
        Vector3 rightV = t.right.normalized;
        Vector3 forwardV = t.forward.normalized;

        float halfW = physicalSize.x * 0.5f;
        float halfH = physicalSize.y * 0.5f;

        // Compute corners using forward direction for vertical axis
        Corners corners = new Corners(
            topRight: center + rightV * halfW + forwardV * halfH,
            topLeft: center - rightV * halfW + forwardV * halfH,
            bottomRight: center + rightV * halfW - forwardV * halfH,
            bottomLeft: center - rightV * halfW - forwardV * halfH
        );

        // Build edges from corner pairs
        Edges edges = new Edges(
            top: new Edge(corners.TopLeft, corners.TopRight),
            left: new Edge(corners.BottomLeft, corners.TopLeft),
            bottom: new Edge(corners.BottomRight, corners.BottomLeft),
            right: new Edge(corners.TopRight, corners.BottomRight)
        );

        return new SurfaceExtent(corners, edges);
    }

    /// <summary>
    /// Returns the device's local axis directions (normalized).
    /// </summary>
    public static AxisDirections GetAxisDirections(Device info)
    {
        Transform t = info.getTransform();
        return new AxisDirections(
            up: t.up.normalized,
            forward: t.forward.normalized,
            right: t.right.normalized
        );
    }
}
