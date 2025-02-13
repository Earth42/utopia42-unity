﻿using UnityEngine.UIElements;

namespace Source.Ui.Utils
{
    public static class TextFields
    {
        public static void SetPlaceHolderForTextField(TextField textField, string placeHolder)
        {
            var label = new Label(placeHolder)
            {
                style =
                {
                    position = Position.Absolute,
                    top = 0,
                    bottom = 0,
                    left = 0,
                    marginLeft = 8,
                    marginRight = 0,
                    marginBottom = 0,
                    marginTop = 1,
                    opacity = 0.8f
                }
            };
            textField.Add(label);
            textField.RegisterValueChangedCallback(e =>
            {
                if (string.IsNullOrEmpty(e.newValue))
                {
                    if (!textField.Contains(label))
                        textField.Add(label);
                }
                else
                {
                    if (textField.Contains(label))
                        textField.Remove(label);
                }
            });
        }
    }
}