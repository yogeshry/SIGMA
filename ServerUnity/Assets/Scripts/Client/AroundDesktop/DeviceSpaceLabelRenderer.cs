using UnityEngine;
using UnityEngine.UI;
using TMPro;

[DisallowMultipleComponent]
public class EdgeSpaceAllEdgesBands : MonoBehaviour
{
    [Header("Assign EdgeSpace (World Space Canvas)")]
    public Canvas edgeSpaceCanvas;

    [Header("Device")]
    public string deviceId;
    public bool debugUseManualSize = true;
    public Vector2 debugManualSizeMeters = new Vector2(0.30f, 0.18f);

    [Header("Bands (meters)")]
    public float bandThicknessMeters = 0.05f;
    public float gapMeters = 0.01f;
    public float liftMeters = 0.002f;
    public float lengthScale = 1.1f;

    [Header("Band Fade Toggles")]
    public bool fadeTop = false;
    [Range(0f, 1f)] public float topAlpha = 0.25f;

    public bool fadeBottom = false;
    [Range(0f, 1f)] public float bottomAlpha = 0.25f;

    public bool fadeLeft = false;
    [Range(0f, 1f)] public float leftAlpha = 0.25f;

    public bool fadeRight = false;
    [Range(0f, 1f)] public float rightAlpha = 0.25f;

    [Header("Textual icons (single glyph/char recommended)")]
    public string topIconText = "Year"; // kept, but top band is NOT shown now
    public string bottomIconText = "\U0000f259";
    public string leftIconText = "🌡";
    public string rightIconText = "🌬";

    [Header("TMP icon styling")]
    public TMP_FontAsset tmpFont;
    public float iconFontSize = 6.0f;
    public Color iconColor = Color.white;

    [Header("Band look")]
    public Color bandColor = new Color(0f, 0f, 0f, 0.55f);

    [Header("Shared band texture (same on all 4)")]
    public Texture sharedBandTexture;
    public RawImage templateRawImage;
    public Image templateImage;

    [Header("Update")]
    public bool followEveryFrame = true;

    // ----------------- Top Year Cards (cropped peek) -----------------
    [Header("Top Year Cards (cropped peek)")]
    public bool showYearCards = true;
    public bool animateYearCards = true;
    public float yearCardsSlideSpeed = 0.25f; // meters/sec

    public Vector2 yearCardSizeMeters = new Vector2(0.14f, 0.06f); // (X width, outward height)
    public float yearCardGapMeters = 0.01f;
    public float yearCardVisibleTabMeters = 0.015f;
    public float yearCardExtraOutMeters = 0.01f;

    [Tooltip("Crop line starts this far OUTWARD from the top edge (meters).")]
    public float yearCropStartMeters = 0.02f;

    [Header("Per-card tuck toggles (peeking vs shown)")]
    public bool tuck2012 = false;
    public bool tuck2013 = false;
    public bool tuck2014 = false;
    public bool tuck2015 = false;

    [Header("Fade tucked year cards")]
    [Range(0f, 1f)] public float tuckedCardAlpha = 0.8f;
    [Header("Fade untucked year cards")]
    [Range(0f, 1f)] public float untuckedCardAlpha = 0.40f; // 60% visible = 40% faded

    public string[] yearTexts = { "2012", "2013", "2014", "2015" };
    // ----------------------------------------------------------------

    // ---- internals ----
    private Transform _canvasTf;
    private Texture _sharedTex;
    private static Texture2D s_FallbackTex;

    private RectTransform _yearMaskRT;
    private BandUI _top, _bottom, _left, _right;
    private BandUI[] _yearCards;

    private float _yearReveal01 = 1f;     // global reveal (0..1)
    private float[] _cardReveal;          // per card reveal (0=tucked, 1=shown)

    private struct BandUI
    {
        public RectTransform rt;
        public RawImage img;
        public TextMeshProUGUI icon;
    }

    // public API
    public void SetDeviceId(string id, bool refreshNow = true)
    {
        deviceId = id;
        if (refreshNow) Refresh();
    }

    // ---- Unity lifecycle ----
    private void Awake()
    {
        if (!edgeSpaceCanvas)
        {
            Debug.LogError("[EdgeSpaceAllEdgesBands] Assign edgeSpaceCanvas.");
            enabled = false;
            return;
        }

        edgeSpaceCanvas.renderMode = RenderMode.WorldSpace;
        _canvasTf = edgeSpaceCanvas.transform;

        // Create base bands first
        _top = EnsureBand(_canvasTf, "Band_Top");
        _bottom = EnsureBand(_canvasTf, "Band_Bottom");
        _left = EnsureBand(_canvasTf, "Band_Left");
        _right = EnsureBand(_canvasTf, "Band_Right");

        // Remove top band visuals (but keep its fade fields for year-cards alpha if you want)
        if (_top.rt != null) _top.rt.gameObject.SetActive(false);

        // Mask container for top year cards (crops at its bottom edge)
        _yearMaskRT = EnsureYearMask(_canvasTf);

        // Create year cards under mask
        int n = (yearTexts != null && yearTexts.Length > 0) ? yearTexts.Length : 4;
        _yearCards = new BandUI[n];
        _cardReveal = new float[n];

        for (int i = 0; i < n; i++)
        {
            _yearCards[i] = EnsureBand(_yearMaskRT, $"YearCard_{i}");

            // full-rect label
            if (_yearCards[i].icon != null)
            {
                var rt = _yearCards[i].icon.rectTransform;
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
            }

            _cardReveal[i] = 1f; // start shown
        }

        // Default FA icons (optional)
        static string FA(int hex) => char.ConvertFromUtf32(hex);
        bottomIconText = FA(0xf073);
        leftIconText = FA(0xe040);
        rightIconText = FA(0xf72e);
    }

    private void LateUpdate()
    {
        if (followEveryFrame) Refresh();
    }

    // ---- Main refresh ----
    public void Refresh()
    {
        if (_canvasTf == null) return;
        if (!TryGetSizeMeters(out Vector2 sizeM)) return;

        EnsureSharedTexture();
        ApplySharedTextureToAll();

        // Canvas pose
        _canvasTf.position = transform.position + transform.up * liftMeters;
        _canvasTf.rotation = Quaternion.LookRotation(transform.up, transform.forward);

        // Common geometry
        float hx = sizeM.x * 0.5f;
        float hz = sizeM.y * 0.5f;
        float distOut = gapMeters + bandThicknessMeters * 0.5f;

        float topBottomLenM = sizeM.x * lengthScale;
        float leftRightLenM = sizeM.y * lengthScale;

        float metersPerCanvasUnit = Mathf.Max(1e-6f, Mathf.Abs(_canvasTf.lossyScale.x));
        float thickU = bandThicknessMeters / metersPerCanvasUnit;
        float topBottomLenU = topBottomLenM / metersPerCanvasUnit;
        float leftRightLenU = leftRightLenM / metersPerCanvasUnit;

        // Precompute band alphas
        float aTop = fadeTop ? Mathf.Clamp01(topAlpha) : 1f;
        float aBottom = fadeBottom ? Mathf.Clamp01(bottomAlpha) : 1f;
        float aLeft = fadeLeft ? Mathf.Clamp01(leftAlpha) : 1f;
        float aRight = fadeRight ? Mathf.Clamp01(rightAlpha) : 1f;

        // Bands (no top band shown)
        Vector3 bottomCenterL = new Vector3(0f, 0f, -hz) + Vector3.back * distOut;
        Vector3 leftCenterL = new Vector3(-hx, 0f, 0f) + Vector3.left * distOut;
        Vector3 rightCenterL = new Vector3(hx, 0f, 0f) + Vector3.right * distOut;

        PlaceBand(_bottom, bottomCenterL, new Vector2(topBottomLenU, thickU), bottomIconText, aBottom);
        PlaceBand(_left, leftCenterL, new Vector2(thickU, leftRightLenU), leftIconText, aLeft);
        PlaceBand(_right, rightCenterL, new Vector2(thickU, leftRightLenU), rightIconText, aRight);

        // Year cards (cropped peek)
        RefreshYearCards(sizeM, metersPerCanvasUnit, aTop);
    }

    private void RefreshYearCards(Vector2 sizeM, float metersPerCanvasUnit, float baseAlphaTop)
    {
        if (_yearMaskRT == null || _yearCards == null || _yearCards.Length == 0) return;

        float hz = sizeM.y * 0.5f;

        // Crop line is yearCropStartMeters outward from the top edge
        Vector3 cropLineCenterL = new Vector3(0f, 0f, hz) + Vector3.forward * Mathf.Max(0f, yearCropStartMeters);
        Vector3 cropLineCenterW = transform.TransformPoint(cropLineCenterL);
        Vector3 cropLineCanvasL = _canvasTf.InverseTransformPoint(cropLineCenterW);

        _yearMaskRT.localPosition = cropLineCanvasL;
        _yearMaskRT.localRotation = Quaternion.identity;

        int count = _yearCards.Length;
        float totalWm = count * yearCardSizeMeters.x + (count - 1) * yearCardGapMeters;

        _yearMaskRT.sizeDelta = new Vector2(
            totalWm / metersPerCanvasUnit,
            (yearCardSizeMeters.y + yearCardExtraOutMeters) / metersPerCanvasUnit
        );

        // Global reveal animation
        float globalTarget = showYearCards ? 1f : 0f;
        if (animateYearCards)
        {
            float denom = Mathf.Max(1e-4f, yearCardSizeMeters.y);
            float step01 = (yearCardsSlideSpeed * Time.deltaTime) / denom;
            _yearReveal01 = Mathf.MoveTowards(_yearReveal01, globalTarget, step01);
        }
        else _yearReveal01 = globalTarget;

        float hiddenCenterY = (yearCardVisibleTabMeters - yearCardSizeMeters.y * 0.5f);
        float shownCenterY = (yearCardSizeMeters.y * 0.5f + yearCardExtraOutMeters);

        float cardWU = yearCardSizeMeters.x / metersPerCanvasUnit;
        float cardHU = yearCardSizeMeters.y / metersPerCanvasUnit;

        // Decide if canvas X is reversed relative to device X
        bool flipX = Vector3.Dot(_canvasTf.right, transform.right) < 0f;

        float stepM = yearCardSizeMeters.x + yearCardGapMeters;

        float startX = flipX
            ? (totalWm * 0.5f - yearCardSizeMeters.x * 0.5f)      // start at "right"
            : (-totalWm * 0.5f + yearCardSizeMeters.x * 0.5f);    // start at "left"

        // Per-card animation speed in normalized space
        float perCardStep01 = 1f;
        if (animateYearCards)
        {
            float denom = Mathf.Max(1e-4f, yearCardSizeMeters.y);
            perCardStep01 = (yearCardsSlideSpeed * Time.deltaTime) / denom;
        }

        for (int i = 0; i < count; i++)
        {
            bool isTucked = GetTuckByIndex(i);

            // Global showYearCards can still force everything tucked if false
            float target = showYearCards ? (isTucked ? 0f : 1f) : 0f;

            if (animateYearCards)
                _cardReveal[i] = Mathf.MoveTowards(_cardReveal[i], target, perCardStep01);
            else
                _cardReveal[i] = target;

            float centerYm = Mathf.Lerp(hiddenCenterY, shownCenterY, _cardReveal[i]);
            float xm = flipX ? (startX - i * stepM) : (startX + i * stepM);

            var card = _yearCards[i];
            if (card.rt == null) continue;

            card.rt.localPosition = new Vector3(xm / metersPerCanvasUnit, centerYm / metersPerCanvasUnit, 0f);
            card.rt.localRotation = Quaternion.identity;
            card.rt.sizeDelta = new Vector2(cardWU, cardHU);

            // Alpha: top-alpha * tucked multiplier
            float a = baseAlphaTop * (isTucked ? Mathf.Clamp01(tuckedCardAlpha) : Mathf.Clamp01(untuckedCardAlpha));

            Color bc = bandColor; bc.a *= a;
            if (card.img) card.img.color = bc;

            string txt = (yearTexts != null && i < yearTexts.Length) ? yearTexts[i] : (2012 + i).ToString();
            if (card.icon)
            {
                card.icon.text = txt;


                Color tc = iconColor; tc.a *= a;
                card.icon.color = tc;

                card.icon.fontSize = iconFontSize;
                if (tmpFont) card.icon.font = tmpFont;
                card.icon.alignment = TextAlignmentOptions.Center;
            }

            _yearCards[i] = card; // (struct copy safety)
        }
    }

    // ---- Helpers: alpha & placement ----
    private void PlaceBand(BandUI b, Vector3 centerLocalXZ, Vector2 sizeU, string iconText, float alpha01)
    {
        if (b.rt == null) return;

        Vector3 centerW = transform.TransformPoint(centerLocalXZ);
        Vector3 centerCanvasL = _canvasTf.InverseTransformPoint(centerW);

        b.rt.localPosition = centerCanvasL;
        b.rt.localRotation = Quaternion.identity;
        b.rt.sizeDelta = sizeU;

        float a = Mathf.Clamp01(alpha01);

        if (b.img)
        {
            Color bc = bandColor;
            bc.a *= a;
            b.img.color = bc;
        }

        if (b.icon)
        {
            b.icon.text = iconText ?? "";
            Color tc = iconColor;
            tc.a *= a;
            b.icon.color = tc;

            b.icon.fontSize = iconFontSize;
            if (tmpFont) b.icon.font = tmpFont;
            b.icon.alignment = TextAlignmentOptions.Center;
        }
    }

    // ---- UI construction ----
    private static RectTransform EnsureYearMask(Transform parent)
    {
        var t = parent.Find("YearCardsMask");
        if (t == null)
        {
            var go = new GameObject("YearCardsMask", typeof(RectTransform), typeof(RectMask2D));
            go.transform.SetParent(parent, false);
            t = go.transform;
        }

        var rt = (RectTransform)t;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.0f); // crop line = bottom
        return rt;
    }

    private static BandUI EnsureBand(Transform parent, string name)
    {
        Transform existing = parent.Find(name);

        RectTransform rt;
        RawImage raw;

        if (existing == null)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(RawImage));
            go.transform.SetParent(parent, false);
            rt = (RectTransform)go.transform;
            raw = go.GetComponent<RawImage>();
        }
        else
        {
            rt = existing as RectTransform;
            if (rt == null)
            {
                Object.Destroy(existing.gameObject);
                var go = new GameObject(name, typeof(RectTransform), typeof(RawImage));
                go.transform.SetParent(parent, false);
                rt = (RectTransform)go.transform;
                raw = go.GetComponent<RawImage>();
            }
            else
            {
                raw = existing.GetComponent<RawImage>() ?? existing.gameObject.AddComponent<RawImage>();
            }
        }

        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        raw.raycastTarget = false;

        // Icon
        Transform iconT = rt.Find("Icon");
        TextMeshProUGUI tmp;

        if (iconT == null)
        {
            var iconGO = new GameObject("Icon", typeof(RectTransform), typeof(TextMeshProUGUI));
            iconGO.transform.SetParent(rt, false);
            tmp = iconGO.GetComponent<TextMeshProUGUI>();
        }
        else
        {
            var iconRT = iconT as RectTransform;
            if (iconRT == null)
            {
                Object.Destroy(iconT.gameObject);
                var iconGO = new GameObject("Icon", typeof(RectTransform), typeof(TextMeshProUGUI));
                iconGO.transform.SetParent(rt, false);
                tmp = iconGO.GetComponent<TextMeshProUGUI>();
            }
            else
            {
                tmp = iconT.GetComponent<TextMeshProUGUI>() ?? iconT.gameObject.AddComponent<TextMeshProUGUI>();
            }
        }

        tmp.raycastTarget = false;
        tmp.enableWordWrapping = false;

        // Keep your mirror-flip behavior
        tmp.rectTransform.localScale = new Vector3(-1f, 1f, 1f);
        return new BandUI { rt = rt, img = raw, icon = tmp };
    }

    // ---- Texture selection & application ----
    private void EnsureSharedTexture()
    {
        if (sharedBandTexture != null) { _sharedTex = sharedBandTexture; return; }
        if (_sharedTex != null && _sharedTex != s_FallbackTex) return;

        if (templateRawImage != null && templateRawImage.texture != null) { _sharedTex = templateRawImage.texture; return; }
        if (templateImage != null && templateImage.sprite != null && templateImage.sprite.texture != null) { _sharedTex = templateImage.sprite.texture; return; }

        // One-time scan fallback
        var raws = _canvasTf.GetComponentsInChildren<RawImage>(true);
        foreach (var r in raws)
        {
            if (!r) continue;
            if (r.name.StartsWith("Band_")) continue;
            if (r.texture != null) { _sharedTex = r.texture; return; }
        }

        if (s_FallbackTex == null)
        {
            s_FallbackTex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            s_FallbackTex.SetPixel(0, 0, Color.white);
            s_FallbackTex.Apply(false, true);
        }
        _sharedTex = s_FallbackTex;
    }

    private void ApplySharedTextureToAll()
    {
        if (_sharedTex == null) return;

        if (_top.img) _top.img.texture = _sharedTex;
        if (_bottom.img) _bottom.img.texture = _sharedTex;
        if (_left.img) _left.img.texture = _sharedTex;
        if (_right.img) _right.img.texture = _sharedTex;

        if (_yearCards == null || _yearCards.Length == 0) return;

        // Copy same look as an existing band (uvRect/material)
        var src = (_bottom.img != null) ? _bottom.img : _left.img;

        for (int i = 0; i < _yearCards.Length; i++)
        {
            var dst = _yearCards[i].img;
            if (!dst) continue;

            dst.texture = _sharedTex;
            if (src != null)
            {
                dst.uvRect = src.uvRect;
                dst.material = src.material;
            }
            dst.raycastTarget = false;
        }
    }

    // ---- Size ----
    private bool TryGetSizeMeters(out Vector2 sizeM)
    {
        if (!string.IsNullOrEmpty(deviceId))
        {
            var device = DeviceManager.GetDevice(deviceId);
            if (device != null)
            {
                sizeM = new Vector2(device.displaySize.WidthInMeters, device.displaySize.HeightInMeters);
                if (sizeM.x > 0f && sizeM.y > 0f) return true;
            }
        }

        if (debugUseManualSize && debugManualSizeMeters.x > 0f && debugManualSizeMeters.y > 0f)
        {
            sizeM = debugManualSizeMeters;
            return true;
        }

        sizeM = Vector2.zero;
        return false;
    }

    // ---- Edge band fade API ----
    public enum EdgeBand { Top, Bottom, Left, Right }

    public void SetBandFade(EdgeBand band, bool fade, float alpha01 = 1f, bool refreshNow = true)
    {
        alpha01 = Mathf.Clamp01(alpha01);

        switch (band)
        {
            case EdgeBand.Top: fadeTop = fade; topAlpha = alpha01; break;
            case EdgeBand.Bottom: fadeBottom = fade; bottomAlpha = alpha01; break;
            case EdgeBand.Left: fadeLeft = fade; leftAlpha = alpha01; break;
            case EdgeBand.Right: fadeRight = fade; rightAlpha = alpha01; break;
        }

        if (refreshNow) Refresh();
    }

    public void SetBandAlpha(EdgeBand band, float alpha01, bool refreshNow = true)
    {
        SetBandFade(band, true, alpha01, refreshNow);
    }

    public void ClearBandFade(EdgeBand band, bool refreshNow = true)
    {
        SetBandFade(band, false, 1f, refreshNow);
    }

    // ---- Year card tuck API ----
    public void SetYearCardTuckByIndex(int index, bool tuck, bool refreshNow = true)
    {
        if (index < 0) return;

        if (index == 0) tuck2012 = tuck;
        else if (index == 1) tuck2013 = tuck;
        else if (index == 2) tuck2014 = tuck;
        else if (index == 3) tuck2015 = tuck;

        if (refreshNow) Refresh();
    }

    public void SetYearCardTuck(int year, bool tuck, bool refreshNow = true)
    {
        int idx = FindYearIndex(year.ToString());
        if (idx >= 0) { SetYearCardTuckByIndex(idx, tuck, refreshNow); return; }

        if (year == 2012) tuck2012 = tuck;
        else if (year == 2013) tuck2013 = tuck;
        else if (year == 2014) tuck2014 = tuck;
        else if (year == 2015) tuck2015 = tuck;

        if (refreshNow) Refresh();
    }

    public void SetAllYearCardsTucked(bool tuck, bool refreshNow = true)
    {
        tuck2012 = tuck;
        tuck2013 = tuck;
        tuck2014 = tuck;
        tuck2015 = tuck;

        if (refreshNow) Refresh();
    }

    public void SetYearTexts(string[] texts, bool refreshNow = true)
    {
        yearTexts = texts;
        if (refreshNow) Refresh();
    }

    private int FindYearIndex(string yearStr)
    {
        if (yearTexts == null) return -1;
        for (int i = 0; i < yearTexts.Length; i++)
            if (string.Equals(yearTexts[i], yearStr, System.StringComparison.Ordinal))
                return i;
        return -1;
    }

    private bool GetTuckByIndex(int i)
    {
        return i switch
        {
            0 => tuck2012,
            1 => tuck2013,
            2 => tuck2014,
            3 => tuck2015,
            _ => false
        };
    }
}
