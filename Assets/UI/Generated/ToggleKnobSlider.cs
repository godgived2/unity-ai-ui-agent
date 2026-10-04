using UnityEngine;
using UnityEngine.UI;

public class ToggleKnobSlider : MonoBehaviour
{
    public RectTransform knob;

    void Start()
    {
        var toggle = GetComponent<Toggle>();
        if (toggle == null) return;
        toggle.onValueChanged.AddListener(Apply);
        Apply(toggle.isOn);
    }

    public void Apply(bool isOn)
    {
        if (knob == null) return;
        if (isOn)
        {
            knob.anchorMin = new Vector2(0.52f, 0.12f);
            knob.anchorMax = new Vector2(0.94f, 0.88f);
        }
        else
        {
            knob.anchorMin = new Vector2(0.06f, 0.12f);
            knob.anchorMax = new Vector2(0.48f, 0.88f);
        }
        knob.offsetMin = Vector2.zero;
        knob.offsetMax = Vector2.zero;
    }
}
