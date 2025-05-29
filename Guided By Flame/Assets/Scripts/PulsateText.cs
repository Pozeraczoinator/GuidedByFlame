using UnityEngine;
using UnityEngine.UI;

public class PulsateText : MonoBehaviour
{
    public float pulseSpeed = 2f;
    public float pulseAmount = 0.1f;
    private Vector3 originalScale;

    void Start()
    {
        originalScale = transform.localScale;
    }

    void Update()
    {
        float scale = 1 + Mathf.Sin(Time.time * pulseSpeed) * pulseAmount;
        transform.localScale = originalScale * scale;
    }
}
