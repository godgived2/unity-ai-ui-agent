using UnityEngine;
using UnityEngine.UI;

public class ToggleTrackColor : MonoBehaviour
{
    public Color onColor = new Color(0.48f, 0.55f, 0.31f, 1f);
    public Color offColor = new Color(0.20f, 0.23f, 0.16f, 1f);
    private Image track;

    void Start()
    {
        track = GetComponent<Image>();
        var toggle = GetComponent<Toggle>();
        if (track == null || toggle == null)
            return;
        
        toggle.onValueChanged.AddListener(Apply);
        Apply(toggle.isOn);
    }

    public void Apply(bool isOn)
    {
        if (track == null)
            return;
        
        track.color = isOn ? onColor : offColor;
    }
}
