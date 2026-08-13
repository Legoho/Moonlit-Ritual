using UnityEngine;
using UnityEngine.UI;

public class SliderManager : MonoBehaviour
{
    [SerializeField]
    private Slider turnSlider;
    [SerializeField]
    private Slider ritualProgressSlider;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        turnSlider=GetComponentsInChildren<Slider>()[0];
        ritualProgressSlider=GetComponentsInChildren<Slider>()[1];
    }


    public void UpdateTurnSlider(int currentTurn,int currentMaxTurn)
    {
        turnSlider.maxValue = currentMaxTurn;
        turnSlider.value = currentTurn;

    }
    public void UpdateRitualProgressSlider(int currentProgress)
    {
        ritualProgressSlider.maxValue = 16;
        ritualProgressSlider.value = currentProgress;
    }
}
