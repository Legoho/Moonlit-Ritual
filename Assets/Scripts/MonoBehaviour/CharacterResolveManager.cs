using UnityEngine;
using UnityEngine.UI;

public class CharacterResolveManager : MonoBehaviour
{
    [SerializeField]
    private Slider slider;
    private Camera mainCamera;
    void Awake()
    {
        if (slider == null)
        {
            slider = GetComponentInChildren<Slider>();
        }
        if(mainCamera == null)
        {
            mainCamera = Camera.main;
        }
    }


    public void UpdateSlider(int currentResolve, int maxResolve)
    {
        if (slider == null) return;
        slider.maxValue = maxResolve;
        slider.value = currentResolve;
    }
    private void Update()
    {
        if (mainCamera != null && slider != null)
        {
            // Make the slider face the camera
            slider.transform.LookAt(mainCamera.transform);
            slider.transform.Rotate(0, 180, 0); // Rotate 180 degrees to face the camera
        }
    }
}
