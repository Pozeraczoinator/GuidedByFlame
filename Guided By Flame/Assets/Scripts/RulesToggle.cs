using UnityEngine;

public class RulesToggle : MonoBehaviour
{
    public GameObject rulesPanel; // przypisany panel z zasadami

    private bool isVisible = false;

    public void ToggleRules()
    {
        isVisible = !isVisible;
        rulesPanel.SetActive(isVisible);
    }
}
