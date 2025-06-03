using UnityEngine;

public class DoorWin : MonoBehaviour
{
    public float winRange = 2f; // Zasięg drzwi
    private GameObject[] doorObjects;
    private Transform nearestDoor;

    void Start()
    {
        doorObjects = GameObject.FindGameObjectsWithTag("Door");
    }

    void Update()
    {
        FindNearestDoor();

        bool nearDoor = nearestDoor != null &&
                        Vector3.Distance(transform.position, nearestDoor.position) <= winRange;



        if (nearDoor && Input.GetKeyDown(KeyCode.E) && KeyPickup.hasKey)
        {
            Debug.Log("Wygrałeś! Byłeś w zasięgu drzwi i miałeś klucz.");
            //WinGame();

            transform.GetComponent<PlayerHealth>()?.Win();
        }
    }

    void FindNearestDoor()
    {
        float closestDistance = Mathf.Infinity;
        Transform closest = null;

        foreach (GameObject door in doorObjects)
        {
            float dist = Vector3.Distance(transform.position, door.transform.position);
            if (dist < closestDistance)
            {
                closestDistance = dist;
                closest = door.transform;
            }
        }

        nearestDoor = closest;
    }

    void WinGame()
    {
        // Tu możesz wczytać scenę zwycięstwa, pokazać ekran itp.
        Application.Quit();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, winRange);
    }
}