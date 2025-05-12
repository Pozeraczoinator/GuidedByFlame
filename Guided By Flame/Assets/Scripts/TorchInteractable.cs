using UnityEngine;
using UnityEngine.Rendering.Universal;

[RequireComponent(typeof(Light2D))]
[RequireComponent(typeof(AudioSource))]
public class TorchInteractable : MonoBehaviour
{
    public KeyCode interactKey = KeyCode.E;
    public float interactRange = 1.5f;

    [SerializeField] private AudioClip torchOnClip;
    [SerializeField] private AudioClip torchOffClip;

    private Light2D torchLight;
    private bool isLit = false;
    private Transform player;
    private AudioSource audioSource;

    void Start()
    {
        torchLight = GetComponent<Light2D>();
        torchLight.enabled = false; // wyłącz światło domyślnie

        audioSource = GetComponent<AudioSource>();
        audioSource.playOnAwake = false;

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

        if (isLit && torchOnClip != null)
        {
            audioSource.clip = torchOnClip;
            audioSource.Play();
        }
        else if (!isLit && torchOffClip != null)
        {
            audioSource.clip = torchOffClip;
            audioSource.Play();
        }
    }
}
