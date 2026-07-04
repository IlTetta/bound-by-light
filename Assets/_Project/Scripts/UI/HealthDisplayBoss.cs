using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class HealthDisplayBoss : MonoBehaviour
{
    public Slider sliders;
    public void SetMaxHealth(int health)
    {
        sliders.maxValue = health;
        sliders.value = health;
    }

    public void SetHealth(int health)
    {
        sliders.value = health;
    }
    void TakeDamage(int damage)
    {
        sliders.value -= damage;

    }

    void GainHealth(int health)
    {
        sliders.value += health;

    }


}