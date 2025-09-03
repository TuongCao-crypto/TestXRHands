using System.Linq;
using UnityEngine;
using UnityEngine.UI;

public static class ToggleGroupUtils
{
    /// <summary>
    /// True if any toggle in this group is ON.
    /// </summary>
    public static bool AnyOn(ToggleGroup group)
    {
        return group != null && group.AnyTogglesOn();
    }

    /// <summary>
    /// Returns the first ON toggle in the group; null if none.
    /// </summary>
    public static Toggle GetSelected(ToggleGroup group)
    {
        return group ? group.ActiveToggles().FirstOrDefault() : null;
    }

    /// <summary>
    /// Returns the index of the ON toggle within an ordered list you manage; -1 if none.
    /// </summary>
    public static int GetSelectedIndex(ToggleGroup group, Toggle[] orderedToggles)
    {
        if (group == null || orderedToggles == null) return -1;
        var on = GetSelected(group);
        if (on == null) return -1;
        for (int i = 0; i < orderedToggles.Length; i++)
            if (orderedToggles[i] == on) return i;
        return -1;
    }

    /// <summary>
    /// Programmatically turns ON a specific toggle (others in the group will turn OFF automatically).
    /// </summary>
    public static void SetSelected(ToggleGroup group, Toggle toSelect)
    {
        if (group == null || toSelect == null) return;
        // Make sure this toggle belongs to the group
        toSelect.group = group;
        // Setting isOn=true will notify the group internally
        toSelect.isOn = true;
    }

    /// <summary>
    /// Ensures the group has at least one toggle ON (uses the first toggle if all are OFF).
    /// </summary>
    public static void EnsureOneOn(ToggleGroup group)
    {
        if (group == null) return;

        // If you want to enforce exactly-one-selected, keep this false.
        group.allowSwitchOff = false;

        if (!group.AnyTogglesOn())
        {
            var first = group.GetComponentsInChildren<Toggle>(true).FirstOrDefault();
            if (first) first.isOn = true;
        }
    }
}
