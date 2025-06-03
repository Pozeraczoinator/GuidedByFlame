using UnityEngine;

public class ChestController : MonoBehaviour
{
    [Header("Sprites")]
    [SerializeField] private Sprite closedSprite;
    [SerializeField] private Sprite openSprite;

    [Header("Player Detection")]
    [SerializeField] private float activationDistance = 2f;

    [Header("Key Drop")]
    [SerializeField] private bool containsKey = false;
    [SerializeField] private GameObject keyObject;

    private SpriteRenderer spriteRenderer;
    private Transform player;
    private bool isOpen = false;

    void Start()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        spriteRenderer.sprite = closedSprite;

        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
            player = playerObj.transform;

        // Klucz powinien być niewidoczny na starcie
        if (keyObject != null)
            keyObject.SetActive(false);
    }

    void Update()
    {
        if (isOpen || player == null)
            return;

        float distance = Vector2.Distance(transform.position, player.position);
        if (distance <= activationDistance && Input.GetKeyDown(KeyCode.E))
        {
            OpenChest();
        }
    }

    private void OpenChest()
    {
        isOpen = true;
        spriteRenderer.sprite = openSprite;

        if (containsKey && keyObject != null)
        {
            keyObject.SetActive(true);
        }
    }
}