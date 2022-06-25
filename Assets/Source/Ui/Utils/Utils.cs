﻿using System;
using Unity.Plastic.Newtonsoft.Json.Serialization;
using UnityEngine;
using UnityEngine.UIElements;

namespace Source.Ui.Utils
{
    public class Utils
    {
        public static void RegisterUiEngagementCallbacksForTextField(TextField textField)
        {
            int? engagementId = null;
            textField.RegisterCallback<FocusInEvent>(evt => { engagementId = GameManager.INSTANCE.EngageUi(); });
            textField.RegisterCallback<FocusOutEvent>(evt =>
            {
                if (engagementId != null)
                {
                    GameManager.INSTANCE.UnEngageUi(engagementId.Value);
                    engagementId = null;
                }
            });
            MouseLook.INSTANCE.cursorLockedStateChanged.AddListener(locked =>
            {
                if (locked && engagementId != null)
                    GameManager.INSTANCE.UnEngageUi(engagementId.Value);
            });
        }

        public static void SetPlaceHolderForTextField(TextField textField, string placeHolder)
        {
            textField.RegisterCallback<FocusInEvent>(evt =>
            {
                if (textField.text == placeHolder)
                    textField.SetValueWithoutNotify("");
            });
            textField.RegisterCallback<FocusOutEvent>(evt =>
            {
                if (string.IsNullOrEmpty(textField.text))
                    textField.SetValueWithoutNotify(placeHolder);
            });
        }

        public static void IncreaseScrollSpeed(ScrollView scrollView, float factor)
        {
            //Workaround to increase scroll speed...
            //There is this issue that verticalPageSize has no effect on speed
            scrollView.RegisterCallback<WheelEvent>((evt) =>
            {
                scrollView.scrollOffset = new Vector2(0, scrollView.scrollOffset.y + factor * evt.delta.y);
                evt.StopPropagation();
            });
        }

        public static VisualElement Create(string uxmlPath)
        {
            return Resources.Load<VisualTreeAsset>(uxmlPath).CloneTree();
        }

        public static void RegisterOnDoubleClick(VisualElement visualElement, Action<MouseDownEvent> action)
        {
            var clicked = 0;
            float clickTime = 0;
            const float clickDelay = 0.5f;
            visualElement.RegisterCallback<MouseDownEvent>(evt =>
            {
                clicked++;
                if (clicked == 1)
                    clickTime = Time.time;
                else if (clicked > 1)
                {
                    if (Time.time - clickTime < clickDelay)
                    {
                        clicked = 0;
                        clickTime = 0;
                        action.Invoke(evt);
                    }
                    else
                    {
                        clicked = 1;
                        clickTime = Time.time;
                    }
                }
            });
        }
    }
}