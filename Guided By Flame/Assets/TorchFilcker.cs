using UnityEngine;
using UnityEngine.Rendering.Universal;
using System.Collections.Generic;

[RequireComponent(typeof(Light2D))]
public class TorchFlicker : MonoBehaviour
{
    private Light2D light2D;
    private float baseIntensity;
    private float baseRadius;

    [Header("Flicker Settings")]
    public float intensityAmplitude = 0.2f;
    public float radiusAmplitude = 0.15f;
    public float flickerSpeed = 1.5f;
    public float smoothSpeed = 5f;

    [Header("Color Flicker")]
    public bool colorFlicker = true;
    public Gradient fireColorGradient;

    private float flickerTimeOffset;

    void Start()
    {
        light2D = GetComponent<Light2D>();
        baseIntensity = light2D.intensity;
        baseRadius = light2D.pointLightOuterRadius;
        flickerTimeOffset = Random.Range(0f, 100f);

        // fallback gradient jeśli nie przypisany
        if (fireColorGradient == null || fireColorGradient.colorKeys.Length < 2)
            fireColorGradient = DefaultFireGradient();
    }

    void Update()
    {
        float t = Time.time * flickerSpeed + flickerTimeOffset;
        float noise = Mathf.PerlinNoise(t, 0f);

        float targetIntensity = baseIntensity + (noise - 0.5f) * intensityAmplitude;
        float targetRadius = baseRadius + (noise - 0.5f) * radiusAmplitude;
        Color targetColor = fireColorGradient.Evaluate(noise);

        light2D.intensity = Mathf.Lerp(light2D.intensity, targetIntensity, Time.deltaTime * smoothSpeed);
        light2D.pointLightOuterRadius = Mathf.Lerp(light2D.pointLightOuterRadius, targetRadius, Time.deltaTime * smoothSpeed);

        if (colorFlicker)
            light2D.color = Color.Lerp(light2D.color, targetColor, Time.deltaTime * smoothSpeed);
    }

    private Gradient DefaultFireGradient()
    {
        Gradient g = new Gradient();

        GradientColorKey[] colorKeys = new GradientColorKey[4];
        colorKeys[0].color = new Color(1f, 0.3f, 0.1f); // Czerwony ogień
        colorKeys[0].time = 0.0f;

        colorKeys[1].color = new Color(1f, 0.6f, 0.2f); // Pomarańcz
        colorKeys[1].time = 0.33f;

        colorKeys[2].color = new Color(1f, 0.85f, 0.3f); // Żółto-pomarańcz
        colorKeys[2].time = 0.66f;

        colorKeys[3].color = new Color(1f, 1f, 0.5f); // Żółty
        colorKeys[3].time = 1.0f;

        GradientAlphaKey[] alphaKeys = new GradientAlphaKey[2];
        alphaKeys[0].alpha = 1.0f;
        alphaKeys[0].time = 0.0f;
        alphaKeys[1].alpha = 1.0f;
        alphaKeys[1].time = 1.0f;

        g.SetKeys(colorKeys, alphaKeys);
        return g;
    }
}