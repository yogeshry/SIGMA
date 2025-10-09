using System;
using System.Collections.Generic;
using UnityEngine;

sealed class PayloadValueValidityComparer : IEqualityComparer<PrimitivePayload>
{
    public bool Equals(PrimitivePayload x, PrimitivePayload y)
    {
        if (ReferenceEquals(x, y)) return true;
        if (x is null || y is null) return false;
        return NearlyEqual(x.Value, y.Value) && x.IsValid == y.IsValid;
    }

    public int GetHashCode(PrimitivePayload p)
    {
        if (p is null) return 0;
        unchecked
        {
            int h = 17;
            h = h * 31 + HashObject(p.Value);
            h = h * 31 + (p.IsValid ? 1 : 0);
            return h;
        }
    }

    // ---- helpers ----
    private static bool NearlyEqual(object a, object b, float eps = 1e-4f)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a == null || b == null) return false;

        if (a is float fa && b is float fb) return Mathf.Abs(fa - fb) <= eps;
        if (a is double da && b is double db) return Math.Abs(da - db) <= eps;

        return Equals(a, b);
    }

    private static int HashObject(object o, float eps = 1e-4f)
    {
        if (o == null) return 0;
        switch (o)
        {
            case float f: return Mathf.RoundToInt(f / eps);
            case double d: return Mathf.RoundToInt((float)(d / eps));
            case bool b: return b ? 1 : 0;
            default: return o.GetHashCode();
        }
    }
}
