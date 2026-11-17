using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class SkillListItem : MonoBehaviour
{
    [SerializeField] TMP_Text skillNameText, skillDetailText;
    [SerializeField] RectTransform rectTransform;
    [SerializeField] RectTransform skillDetailTextRectTransform;
    public void SetUp(string skillName, string skillDetail)
    {
        skillNameText.text = skillName;
        skillDetailText.text = skillDetail;
    }

}
