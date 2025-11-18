using UnityEngine;

public static class Utils
{
    /// <summary>
    /// Updates the outline LineRenderer (child named "outline") of the given runtime image target.
    /// The outline is drawn as a rectangle based on the device's physical size.
    /// </summary>
    /// <param name="transform">The parent transform that should contain a child named "outline".</param>
    /// <param name="deviceId">ID of the device to fetch its physical size.</param>
    public static void UpdateDeviceOutline(Transform transform, string deviceId)
    {
        if (transform == null)
        {
            Debug.LogError("[WS] transform transform is null.");
            return;
        }

        if (deviceId == null)
        {
            Debug.LogError("[WS] deviceId is null.");
        }

        // Try to find the "outline" child with a LineRenderer
        var outlineTransform = transform.Find("outline");
        if (outlineTransform == null)
        {
            Debug.LogWarning($"[WS] Child 'outline' not found under {transform.name} for device {deviceId}.");
            return;
        }

        var outline = outlineTransform.GetComponent<LineRenderer>();
        if (outline == null)
        {
            Debug.LogWarning($"[WS] No LineRenderer component on child 'outline' for device {deviceId}.");
            return;
        }

        // Get device physical size
        var device = DeviceManager.GetDevice(deviceId);
        if (device == null)
        {
            Debug.LogWarning($"[WS] Device not found for id {deviceId}.");
            return;
        }

        // Convert DisplaySize to Vector2
        Vector2 size = new Vector2(device.displaySize.WidthInMeters, device.displaySize.HeightInMeters);

        // Define rectangle corners in local space (X-Z plane, centered at origin)
        outline.positionCount = 5;
        outline.SetPosition(0, new Vector3(-size.x / 2, 0, size.y / 2));
        outline.SetPosition(1, new Vector3(size.x / 2, 0, size.y / 2));
        outline.SetPosition(2, new Vector3(size.x / 2, 0, -size.y / 2));
        outline.SetPosition(3, new Vector3(-size.x / 2, 0, -size.y / 2));
        outline.SetPosition(4, new Vector3(-size.x / 2, 0, size.y / 2)); // close loop

        outline.enabled = true;

    }

    public static void DisableDeviceOutline(Transform transform)
    {
        if (transform == null)
        {
            Debug.LogError("[WS] transform transform is null.");
            return;
        }
        // Try to find the "outline" child with a LineRenderer
        var outlineTransform = transform.Find("outline");
        if (outlineTransform == null)
        {
            Debug.LogWarning($"[WS] Child 'outline' not found under {transform.name}.");
            return;
        }
        var outline = outlineTransform.GetComponent<LineRenderer>();
        if (outline == null)
        {
            Debug.LogWarning($"[WS] No LineRenderer component on child 'outline'.");
            return;
        }
        outline.enabled = false;
    }

    /// <summary>
    /// Updates the outline LineRenderer (child named "outline") of the given runtime image target.
    /// The outline is drawn as a rectangle based on the device's physical size.
    /// </summary>
    /// <param name="transform">The parent transform that should contain a child named "outline".</param>
    /// <param name="deviceId">ID of the device to fetch its physical size.</param>
    public static void UpdateDeviceSpaceLabel(Transform transform, string deviceId)
    {
        if (transform == null)
        {
            Debug.LogError("[WS] transform transform is null.");
            return;
        }

        if (deviceId == null)
        {
            Debug.LogError("[WS] deviceId is null.");
        }

        // Try to find the "outline" child with a LineRenderer
        var outlineTransform = transform.Find("outline");
        if (outlineTransform == null)
        {
            Debug.LogWarning($"[WS] Child 'outline' not found under {transform.name} for device {deviceId}.");
            return;
        }

        var outline = outlineTransform.GetComponent<LineRenderer>();
        if (outline == null)
        {
            Debug.LogWarning($"[WS] No LineRenderer component on child 'outline' for device {deviceId}.");
            return;
        }

        // Get device physical size
        var device = DeviceManager.GetDevice(deviceId);
        if (device == null)
        {
            Debug.LogWarning($"[WS] Device not found for id {deviceId}.");
            return;
        }

        // Convert DisplaySize to Vector2
        Vector2 size = new Vector2(device.displaySize.WidthInMeters, device.displaySize.HeightInMeters);




        // ----------------- NEW: 45° outward corner lines (5 cm) -----------------

        var tl = new Vector3(-size.x / 2f, 0f, size.y / 2f);
        var tr = new Vector3(size.x / 2f, 0f, size.y / 2f);
        var br = new Vector3(size.x / 2f, 0f, -size.y / 2f);
        var bl = new Vector3(-size.x / 2f, 0f, -size.y / 2f);
        // ---- 45° outward corner ticks (5 cm), drawn in world space ----
        const float tickLen = 0.15f; // 5 cm

        // Reuse outline look
        float w = 0.005f; // outline.widthMultiplier;
        var mat = outline.sharedMaterial ?? new Material(Shader.Find("Sprites/Default"));
        var col = outline.startColor;

        // holder (rebuilt each call to avoid duplicates)
        var ticksRoot = transform.Find("corner45") ?? new GameObject("corner45").transform;
        if (ticksRoot.parent != transform) ticksRoot.SetParent(transform, false);
        for (int i = ticksRoot.childCount - 1; i >= 0; --i) Object.DestroyImmediate(ticksRoot.GetChild(i).gameObject);

        void MakeTick(string name, Vector3 cornerLocal)
        {
            var go = new GameObject(name);
            go.transform.SetParent(ticksRoot, false);

            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.positionCount = 2;
            lr.widthMultiplier = w;
            lr.numCornerVertices = 2;
            lr.numCapVertices = 2;
            lr.material = mat;
            lr.startColor = lr.endColor = col;

            // world start at the corner; outward dir = from center to corner (45° on XZ)
            Vector3 startW = transform.TransformPoint(cornerLocal);
            Vector3 dirW = (startW - transform.position).normalized;
            Vector3 endW = startW + dirW * tickLen;

            lr.SetPosition(0, startW);
            lr.SetPosition(1, endW);
        }

        // corners you already computed above
        MakeTick("tick_TL", tl);
        MakeTick("tick_TR", tr);
        MakeTick("tick_BR", br);
        MakeTick("tick_BL", bl);

        // -----------------------------------------------------------------------


        // ---- Edge labels outside the rectangle ---------------------------------
        var labelsRoot = transform.Find("edgeLabels") ?? new GameObject("edgeLabels").transform;
        if (labelsRoot.parent != transform) labelsRoot.SetParent(transform, false);
        // rebuild each call (avoids duplicates)
        for (int i = labelsRoot.childCount - 1; i >= 0; --i) Object.DestroyImmediate(labelsRoot.GetChild(i).gameObject);


        // midpoints of each edge (local)
        Vector3 topMid = (tl + tr) * 0.5f;
        Vector3 bottomMid = (bl + br) * 0.5f;
        Vector3 leftMid = (tl + bl) * 0.5f;
        Vector3 rightMid = (tr + br) * 0.5f;

        // --- tweakables (meters) ---
        const float arrowGap = 0.015f;  // small gap between text and arrow start
        const float arrowLen = 0.10f;   // shaft length
        const float arrowHead = 0.02f;   // head size
        const float arrowBelow = 0.04f;   // how far *under* the text the arrow is placed

        // outward directions (local)
        Vector3 topOut = Vector3.forward, bottomOut = Vector3.back, leftOut = Vector3.left, rightOut = Vector3.right;

        const float labelOffset = 0.09f; // 7 cm outside the edge
        //Font builtin = Resources.GetBuiltinResource<Font>("Arial.ttf");

        void MakeLabel(string name, string text, Vector3 localMid, Vector3 localOut, Vector3 localTangent, bool withArrow = true, bool isAbove = false, float arrowLength = arrowLen)
        {
            var go = new GameObject(name);
            go.transform.SetParent(labelsRoot, false);

            var tm = go.AddComponent<TextMesh>();
            tm.text = text;
            //tm.font = builtin;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.color = outline.startColor;
            tm.characterSize = 0.02f;    // tune for your world scale
            tm.fontSize = 14;

            // place outside the edge in world space
            Vector3 worldPos = transform.TransformPoint(localMid + localOut.normalized * labelOffset);
            go.transform.position = worldPos;

            // orient with the plane (or face the camera if you prefer)

            // make the text coplanar with the rectangle and aligned to the edge
            Vector3 planeNormalW = transform.up;                                  // rectangle's normal (local +Y)
            Vector3 tangentW = transform.TransformDirection(localTangent);     // edge direction
            Vector3 upW = Vector3.Cross(planeNormalW, tangentW);          // ensures Right == tangent

            if (upW.sqrMagnitude < 1e-8f) upW = transform.forward;                  // degenerate fallback
            go.transform.rotation = Quaternion.LookRotation(-planeNormalW, upW);     // coplanar + aligned
                                                                                     // To face camera instead, uncomment:
                                                                                     // if (Camera.main) go.transform.rotation =
                                                                                     //   Quaternion.LookRotation(go.transform.position - Camera.main.transform.position, transform.up);

            if (withArrow)
            {
                DrawArrowUnderText(name + "_arrow", go.transform, planeNormalW, -tangentW, upW,
                                   outline.startColor, Mathf.Max(0.001f, outline.widthMultiplier * 0.008f), isAbove);
            }
        }


        // Reuse outline's look for consistency
        void SetupLine(LineRenderer lr, Color color, float width)
        {
            lr.material = outline.material;
            lr.widthMultiplier = width;
            lr.startColor = lr.endColor = color;
            lr.useWorldSpace = true;
            lr.numCapVertices = 4;
        }

        void DrawArrowUnderText(string name, Transform textTf,
                                Vector3 planeNormalW, Vector3 tangentW, Vector3 upW,
                                Color color, float width, bool placeAbove = false)
        {
            tangentW = tangentW.normalized;
            upW = upW.normalized;

            // offset sign: below = -1, above = +1
            float dir = placeAbove ? 1f : -1f;

            // Center line parallel to text, shifted along in-plane "up"
            Vector3 center = textTf.position + upW * (dir * arrowBelow);
            Vector3 start = center - tangentW * (arrowLen * 0.5f);
            Vector3 end = center + tangentW * (arrowLen * 0.5f);

            // Shaft
            var shaftGO = new GameObject(name + "_shaft");
            shaftGO.transform.SetParent(labelsRoot, false);
            var shaft = shaftGO.AddComponent<LineRenderer>();
            SetupLine(shaft, color, width);
            shaft.positionCount = 2;
            shaft.SetPositions(new[] { start, end });

            // Arrow head at the forward end (along text direction)
            Vector3 side = Vector3.Cross(planeNormalW, tangentW).normalized;
            Vector3 tip = end;
            Vector3 h1 = tip - tangentW * arrowHead + side * (arrowHead * 0.6f);
            Vector3 h2 = tip - tangentW * arrowHead - side * (arrowHead * 0.6f);

            var h1GO = new GameObject(name + "_head1");
            h1GO.transform.SetParent(labelsRoot, false);
            var lr1 = h1GO.AddComponent<LineRenderer>();
            SetupLine(lr1, color, width);
            lr1.positionCount = 2;
            lr1.SetPositions(new[] { tip, h1 });

            var h2GO = new GameObject(name + "_head2");
            h2GO.transform.SetParent(labelsRoot, false);
            var lr2 = h2GO.AddComponent<LineRenderer>();
            SetupLine(lr2, color, width);
            lr2.positionCount = 2;
            lr2.SetPositions(new[] { tip, h2 });
        }

        // midpoints/tangents per edge (local)
        MakeLabel("label_top", "Year", topMid, topOut, Vector3.left);
        MakeLabel("label_bottom", "Time - Month", bottomMid, bottomOut, Vector3.left, true, true);
        MakeLabel("label_left", "Maximum Temperature", leftMid, leftOut, Vector3.back);
        MakeLabel("label_right", "Wind Speed", rightMid, rightOut, Vector3.back,true, true);

    }
}
