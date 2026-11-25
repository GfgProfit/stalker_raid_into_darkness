using System;
using RaidIntoDarkness.DI;
using RaidIntoDarkness.Settings;
using UnityEngine;

namespace RaidIntoDarkness.UI
{
    [DefaultExecutionOrder(-1000)]
    [DisallowMultipleComponent]
    public sealed class SettingsInstaller : MonoBehaviour
    {
        private static IContainer _application;

        [SerializeField] private MonoBehaviour[] _targets = Array.Empty<MonoBehaviour>();

        private void Awake()
        {
            if (_application == null)
            {
                _application = new DiContainer();
                _application.Bind<IGameSettings>(_ => new GameSettingsStore(SettingsFile.ResolvePath(Application.isEditor, Application.dataPath, Application.persistentDataPath, Application.productName, GameSettingsStore.FILE_NAME))).AsSingle();
                Application.quitting += DisposeApplication;
            }

            foreach (MonoBehaviour target in _targets)
            {
                if (target is SettingsMenuPages menu)
                {
                    foreach (SettingsPageView page in menu.Pages)
                    {
                        _application.Inject(page);
                    }
                }
                else if (target != null)
                {
                    _application.Inject(target);
                }
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void DisposeApplication()
        {
            Application.quitting -= DisposeApplication;
            _application?.Dispose();
            _application = null;
        }
    }
}
