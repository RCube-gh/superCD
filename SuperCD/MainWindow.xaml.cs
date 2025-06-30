using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace SuperCD
{
    public partial class MainWindow : Window
    {
        private readonly string fixedPrefix = "> ";
        private bool isUpdating = false;

        private void CommandBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                ConfirmCurrentInput();
            }
        }

        private void ConfirmCurrentInput()
        {
            if (isUpdating) return;
            isUpdating = true;

            // Extract current text
            var lastBlock = CommandBox.Document.Blocks.LastBlock as Paragraph;
            if (lastBlock == null) return;

            string fullLine = new TextRange(lastBlock.ContentStart, lastBlock.ContentEnd).Text.TrimEnd('\r', '\n');
            string input = fullLine.StartsWith(fixedPrefix) ? fullLine.Substring(fixedPrefix.Length) : "";

            if (string.IsNullOrWhiteSpace(input))
            {
                isUpdating = false;
                return;
            }

            // Replace current paragraph with confirmed line (green, no prefix)
            Paragraph confirmedPara = new Paragraph
            {
                Margin = new Thickness(0)
            };
            confirmedPara.Inlines.Add(new Run(fixedPrefix + input)
            {
                Foreground = (Brush)new BrushConverter().ConvertFrom("#66FF66"),
                FontFamily = new FontFamily("Agave Nerd Font"),
                FontSize = 16
            });

            // Insert new editable line
            Paragraph newLine = new Paragraph
            {
                Margin = new Thickness(0)
            };
            newLine.Inlines.Add(new Run(fixedPrefix)
            {
                Foreground = Brushes.Gray,
                FontFamily = new FontFamily("Agave Nerd Font"),
                FontSize = 16
            });

            // Replace and insert
            CommandBox.Document.Blocks.Remove(lastBlock);
            CommandBox.Document.Blocks.Add(confirmedPara);
            CommandBox.Document.Blocks.Add(newLine);

            CommandBox.CaretPosition = newLine.ContentEnd;
            CommandBox.ScrollToEnd();

            isUpdating = false;
        }



    }
}
