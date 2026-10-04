using System.Reflection;
using UnityEngine;

[DisallowMultipleComponent]
public class ArrowTouchPrecision : MonoBehaviour
{
    private static readonly FieldInfo HitRadiusField =
        typeof(ArrowPathController).GetField(
            "hitRadius",
            BindingFlags.Instance | BindingFlags.NonPublic
        );

    private static readonly FieldInfo HeadPaddingField =
        typeof(ArrowPathController).GetField(
            "headHitPadding",
            BindingFlags.Instance | BindingFlags.NonPublic
        );

    public void ApplyNow()
    {
        ArrowPathController arrow =
            GetComponent<ArrowPathController>();

        if (arrow == null)
            return;

        LineRenderer line =
            arrow.GetComponent<LineRenderer>();

        float bodyHalfWidth =
            line != null
                ? line.startWidth * 0.5f
                : 0.09f;

        if (HitRadiusField != null)
        {
            HitRadiusField.SetValue(
                arrow,
                Mathf.Max(0.01f, bodyHalfWidth + 0.01f)
            );
        }

        if (HeadPaddingField != null)
        {
            HeadPaddingField.SetValue(
                arrow,
                0.01f
            );
        }
    }
}
