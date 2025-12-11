using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

public class Abilities : MonoBehaviour
{
    [Header("Trap Ability")]
    public Image abilityImage1;
    public Text abilityText1;
    public KeyCode ability1Key;
    public float ability1Cooldown = 5; 
    private bool isAbility1Cooldown = false;
    private float currentAbility1Cooldown;

    private void Start()
    {
        abilityImage1.fillAmount = 0;
        abilityText1.text = "";
    }

    private void Update()
    {
        Ability1Input();
        AbilityCooldown(ref currentAbility1Cooldown, ability1Cooldown, ref isAbility1Cooldown, abilityImage1, abilityText1);
    }

    private void AbilityCooldown(ref float currentCooldown, float maxCooldown, ref bool isCooldown, Image skillImage, Text skillText)
    {
        if (isCooldown)
        {
            currentCooldown -= Time.deltaTime;

            if (currentCooldown <= 0f)
            {
                isCooldown = false;
                currentCooldown = 0f;

                if (skillImage != null)
                {
                    skillImage.fillAmount = 0f;
                }
                if (skillText != null)
                {
                    skillText.text = "";
                }
            }
            else
            {
                if (skillImage != null)
                {
                    skillImage.fillAmount = currentCooldown / maxCooldown;
                }
                if (skillText != null)
                {
                    skillText.text = Math.Ceiling(currentCooldown).ToString();
                }
            }
        }
    }

    private void Ability1Input()
    {
        if (Input.GetKeyDown(ability1Key) && !isAbility1Cooldown)
        {
            isAbility1Cooldown = true;
            currentAbility1Cooldown = ability1Cooldown;
        }
    }
}
