using UnityEngine;
using UnityEngine.SceneManagement;

namespace AbsoluteZero.Core.Session
{
    public sealed class SceneTransitionService : ISceneTransitionService
    {
        readonly string _titleSceneName;

        public SceneTransitionService(string titleSceneName)
        {
            _titleSceneName = titleSceneName;
        }

        public void LoadTitleScene()
        {
            SceneManager.LoadScene(_titleSceneName);
            Debug.Log($"[SceneTransition] Loaded: {_titleSceneName}");
        }
    }
}
