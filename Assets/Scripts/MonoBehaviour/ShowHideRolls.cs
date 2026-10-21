using UnityEngine;
using UnityEngine.UI;

public class ShowHideRolls : MonoBehaviour
{
    [SerializeField]
    private GameObject RollPanel;

    private void Start()
    {
        if (RollPanel == null)
        {
            RollPanel = this.gameObject;
        }

    }

    public void ToggleVisibility()
    {
       if(RollPanel != null)
        {
            if(RollPanel.activeSelf)
            {
                Debug.Log("Hiding Roll Panel");
            }
            else
            {
                Debug.Log("Showing Roll Panel");
            }
            RollPanel.SetActive(!RollPanel.activeSelf);
        }
    }
}
