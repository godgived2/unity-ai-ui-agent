using UnityEngine;
using UnityEngine.UI;

public class EngineTempPulse : MonoBehaviour
{
    public float speed = 1.0f;
    public Image target;
    public Color color1 = Color.red;
    public Color color2 = Color.yellow;

    private void Start()
    {
        if (target == null)
            return;

        InvokeRepeating("PulseColor", 0f, 1f / speed);
    }

    private void PulseColor()
    {
        if (target == null)
            return;

        float t = Mathf.PingPong(Time.time * speed, 1f);
        target.color = Color.Lerp(color1, color2, t);
    }
}
