using UnityEngine;
using UnityEngine.UI;

public class DeathScreenUI : MonoBehaviour
{
    private CanvasGroup canvasGroup;
    public float fadeSpeed = 1.5f;

    void Awake()
    {
        canvasGroup = GetComponent<CanvasGroup>();
        canvasGroup.alpha = 0f;
    }

    void OnEnable()
    {
        StartCoroutine(FadeIn());
    }

    System.Collections.IEnumerator FadeIn()
    {
        while (canvasGroup.alpha < 1f)
        {
            canvasGroup.alpha += Time.deltaTime * fadeSpeed;
            yield return null;
        }
    }
}
