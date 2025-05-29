using UnityEngine;
using UnityEngine.Rendering.Universal;
using System.Collections;

[RequireComponent(typeof(Light2D))]
[RequireComponent(typeof(AudioSource))]
[RequireComponent(typeof(SpriteRenderer))]
public class TorchInteractable : MonoBehaviour
{
    public KeyCode interactKey = KeyCode.E;
    public float interactRange = 1.5f;

    [SerializeField] private AudioClip torchOnClip;
    [SerializeField] private AudioClip torchOffClip;

    [SerializeField] private Sprite torchOffSprite;
    [SerializeField] private Sprite torchOnSprite;

    private Light2D torchLight;
    private bool isLit = false;
    private Transform player;
    private AudioSource audioSource;
    private SpriteRenderer spriteRenderer;
    private Coroutine autoExtinguishCoroutine;

    void Start()
    {
        torchLight = GetComponent<Light2D>();
        torchLight.enabled = false;

        audioSource = GetComponent<AudioSource>();
        audioSource.playOnAwake = false;

        spriteRenderer = GetComponent<SpriteRenderer>();
        spriteRenderer.sprite = torchOffSprite; // domyślnie zgaszona

        player = GameObject.FindGameObjectWithTag("Player")?.transform;
    }

    void Update()
    {
        if (player == null) return;

        float distance = Vector2.Distance(transform.position, player.position);
        if (distance <= interactRange && Input.GetKeyDown(interactKey))
        {
            ToggleTorch();
        }
    }

    private void ToggleTorch()
    {
        isLit = !isLit;
        torchLight.enabled = isLit;

        // Zmieniamy sprite w zależności od stanu
        spriteRenderer.sprite = isLit ? torchOnSprite : torchOffSprite;

        if (isLit)
        {
            if (torchOnClip != null)
            {
                audioSource.clip = torchOnClip;
                audioSource.Play();
            }

            if (autoExtinguishCoroutine != null)
                StopCoroutine(autoExtinguishCoroutine);

            autoExtinguishCoroutine = StartCoroutine(AutoExtinguishTorch());
        }
        else
        {
            if (torchOffClip != null)
            {
                audioSource.clip = torchOffClip;
                audioSource.Play();
            }

            if (autoExtinguishCoroutine != null)
            {
                StopCoroutine(autoExtinguishCoroutine);
                autoExtinguishCoroutine = null;
            }
        }
    }

    private IEnumerator AutoExtinguishTorch()
    {
        yield return new WaitForSeconds(60f);
        if (isLit)
        {
            ToggleTorch();
        }
    }
}
