using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Free.H2D;

namespace Free.Checkers
{
    public class TQ_TipUI : ZFMonoBehaviour
    {
        [System.Serializable]
        public class TQ_TipSprite
        {
            public GameLanguageType languageType;
            public Sprite tipImage;
        }

        [SerializeField] protected List<TQ_TipSprite> tipImages = new List<TQ_TipSprite>();
        [SerializeField] Image tipImageComponent;
        private CanvasGroup _canvasGroup;

        protected virtual void Awake()
        {
            _canvasGroup = GetComponent<CanvasGroup>();
            tipImageComponent.sprite = tipImages[0].tipImage;
        }
        protected virtual void ShowTipPanel()
        {
            _canvasGroup.alpha = 1;
        }

        public virtual void Hide()
        {
            _canvasGroup.alpha = 0;
        }

        public virtual void Toggle()
        {
            _canvasGroup.alpha = 1 - _canvasGroup.alpha;
        }
    }
}
