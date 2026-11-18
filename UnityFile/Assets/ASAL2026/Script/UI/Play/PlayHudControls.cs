using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;

namespace ASAL
{
    internal static class PlayHudControls
    {
        internal static void SetOptions(TMP_Dropdown dropdown, IEnumerable<string> values, int selection)
        {
            dropdown.ClearOptions();
            dropdown.AddOptions(values.ToList());
            dropdown.SetValueWithoutNotify(Mathf.Max(0, selection));
            dropdown.RefreshShownValue();
            dropdown.interactable = dropdown.options.Count > 0;
        }

        internal static GameObject AddRow(GameObject template)
        {
            var row = Object.Instantiate(template, template.transform.parent, false);
            row.SetActive(true);
            return row;
        }

        internal static void ClearRows(GameObject template)
        {
            foreach (Transform child in template.transform.parent)
                if (child.name == template.name + "(Clone)")
                {
                    child.gameObject.SetActive(false);
                    Object.Destroy(child.gameObject);
                }
        }

        internal static GameObject Find(GameObject root, string name) =>
            root.GetComponentsInChildren<Transform>(true).First(child => child.name == name).gameObject;
        internal static TMP_Text Text(GameObject root, string name) => Find(root, name).GetComponent<TMP_Text>();

        internal static void SetText(TMP_Text label, string value)
        {
            label.richText = false;
            label.text = value;
        }
    }
}
