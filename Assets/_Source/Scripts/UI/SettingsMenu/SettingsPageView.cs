using UnityEngine;
using UnityEngine.UI;

namespace RaidIntoDarkness.UI
{
    public abstract class SettingsPageView : MonoBehaviour
    {
        [SerializeField] private string _title;
        [SerializeField] private Button _tabButton;
        [SerializeField] private ScrollRect _scroll;
        [SerializeField] private Selectable _initialControl;

        internal string Title => _title;
        internal Button TabButton => _tabButton;
        internal ScrollRect Scroll => _scroll;
        internal Selectable InitialControl => _initialControl != null && _initialControl.isActiveAndEnabled && _initialControl.IsInteractable() ? _initialControl : _tabButton;

        internal abstract ISettingsPageViewModel Open();

        internal virtual void Close()
        {
        }
    }
}
