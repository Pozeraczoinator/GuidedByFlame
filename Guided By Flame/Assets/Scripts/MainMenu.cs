using UnityEngine;
using UnityEngine.SceneManagement;

public class MainMenu : MonoBehaviour
{

        public void StartGame()
    {
        Debug.Log("Kliknięto StartGame!");
        SceneManager.LoadScene("SampleScene"); // Zmień na nazwę sceny z grą
    }

    public void QuitGame()
    {
        Debug.Log("Wyjście z gry");
        Application.Quit();
    }
    
}
