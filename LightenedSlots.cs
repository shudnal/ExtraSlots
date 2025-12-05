using System.Collections.Generic;
using static ExtraSlots.ExtraSlots;
using static ExtraSlots.Slots;

namespace ExtraSlots
{
    public static class LightenedSlots
    {
        private static readonly List<int> m_affectedRows = new List<int>();

        public static bool IsEnabled => lightenedSlotsStartIndex.Value != 0;
        
        public static float WeightFactor => lightenedSlotsWeightFactor.Value;

        public static bool IsRowAffected(int row) => IsEnabled && m_affectedRows.Contains(row);

        public static void UpdateState()
        {
            UpdateAffectedRows();
            InventoryInteraction.UpdateTotalWeight();
        }

        public static void UpdateAffectedRows()
        {
            m_affectedRows.Clear();

            if (!IsEnabled)
                return;

            if (!SlotsProgression.IsPlayerKeyItemConditionMet(lightenedSlotsPlayerKey.Value, lightenedSlotsItemDiscovered.Value))
                return;

            for (int i = 0; i < InventoryHeightPlayer; i++)
            {
                if (lightenedSlotsOnlyExtraRows.Value && i < 4)
                    continue;
                
                if (lightenedSlotsStartIndex.Value < 0 && i >= lightenedSlotsStartIndex.Value + InventoryHeightPlayer)
                    m_affectedRows.Add(i);
                else if (lightenedSlotsStartIndex.Value > 0 && i >= lightenedSlotsStartIndex.Value - 1)
                    m_affectedRows.Add(i);
            }
        }
    }
}
