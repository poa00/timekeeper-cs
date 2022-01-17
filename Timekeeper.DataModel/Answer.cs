﻿namespace Timekeeper.DataModel
{
    public class Answer
    {
        public string TitleMarkdown { get; set; }

        public string TitleHtml { get; set; }

        public bool IsCorrect { get; set; }
        public string Letter { get; set; }
        public int Count { get; set; }
        public double Ratio { get; set; }

        public string ExplanationMarkdown { get; set; }
    }
}