using UnityEngine;
using UnityEngine.SceneManagement;

public class DoorToNextLevel : MonoBehaviour
{
    public float interactRange = 2f;
    public string nextSceneName; // Nazwa sceny, do której ma przenieść

    private Transform player;

    void Start()
    {
        player = GameObject.FindGameObjectWithTag("Player")?.transform;

        if (string.IsNullOrEmpty(nextSceneName))
        {
            Debug.LogWarning("Nie ustawiono nazwy kolejnej sceny w DoorToNextLevel.");
        }
    }

    void Update()
    {
        if (player == null) return;

        float distance = Vector3.Distance(transform.position, player.position);

        if (distance <= interactRange && Input.GetKeyDown(KeyCode.E) && KeyPickup.hasKey)
        {
            Debug.Log("Przechodzisz do kolejnego poziomu: " + nextSceneName);
            SceneManager.LoadScene(nextSceneName);
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, interactRange);
    }
}
