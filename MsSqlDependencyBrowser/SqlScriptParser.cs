﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SqlScriptParser
{
    interface ITextProcessor 
    {
        string Process(string text);
    }

    abstract class TextProcessorDecorator : ITextProcessor
    {
        ITextProcessor textProcessor;
        public TextProcessorDecorator(ITextProcessor textProcessor)
        {
            this.textProcessor = textProcessor;
        }
        public string Process(string text)
        {
            List<TextBlock> blocks = SplitText(text);
            blocks.ForEach(block => block.text = block.isMatch ? ProcessText(block.text) : textProcessor.Process(block.text));
            return blocks.Aggregate(string.Empty, (first, second) => first + second.text);
        }
        protected abstract List<TextBlock> SplitText(string text);
        protected abstract string ProcessText(string text);
    }

    class DependencyProcessor : ITextProcessor
    {
        private Dictionary<string, string> depList;

        public DependencyProcessor(Dictionary<string, string> depList)
        {
            this.depList = depList;
        }

        public virtual string Process(string text)
        {
            var words = Regex.Matches(text, @"\w+|\W+")
                                .Cast<Match>()
                                .Select(m => m.Value)
                                .ToList();
            var builder = new StringBuilder();
            words.ForEach(word => builder.Append(MakeReplacement(word)));
            return builder.ToString();
        }

        string MakeReplacement(string word)
        {
            string replace;
            if (depList.TryGetValue(word.ToLower(), out replace))
            {
                return replace;
            }
            return word;
        }
    }

    class KeywordProcessor : TextProcessorDecorator
    {
        private HashSet<string> keywords;
        string template;

        public KeywordProcessor(ITextProcessor textProcessor, HashSet<string> keywords, string template) : base(textProcessor)
        {
            this.keywords = keywords;
            this.template = template;
        }

        protected override string ProcessText(string text)
        {
            return string.Format(template, text);
        }

        protected override List<TextBlock> SplitText(string text)
        {
            List<TextBlock> blocks;
            try
            {
                blocks = Regex.Matches(text, @"\w+|\W+", RegexOptions.Multiline, new TimeSpan(0, 0, 1))
                    .Cast<Match>()
                    .Select(m => new TextBlock(keywords.Contains(m.Value.ToLower()), m.Index, m.Length, m.Value))
                    .ToList();
            }
            catch (RegexMatchTimeoutException)
            {
                blocks = new List<TextBlock>();
                blocks.Add(new TextBlock(false, 0, text.Length, text));
            }
            return blocks;
        }
    }

    class BlockProcessor : TextProcessorDecorator
    {
        private string regex;
        private string template;

        public BlockProcessor(ITextProcessor textProcessor, string regex, string template) : base(textProcessor)
        {
            this.regex = regex;
            this.template = template;
        }

        protected override string ProcessText(string text)
        {
            return string.Format(template, text);
        }

        protected override List<TextBlock> SplitText(string text)
        {
            List<TextBlock> blocks = new List<TextBlock>(); ;
            try
            {
                blocks = Regex.Matches(text, regex, RegexOptions.Multiline, new TimeSpan(0, 0, 1))
                    .Cast<Match>()
                    .Select(m => new TextBlock(true, m.Index, m.Length, m.Value))
                    .ToList();
            } catch (RegexMatchTimeoutException)
            {
                blocks.Add(new TextBlock(false, 0, text.Length, text));
                return blocks;
            }
            if (blocks.Count == 0)
            {
                blocks.Add(new TextBlock(false, 0, text.Length, text));
                return blocks;
            }
            appendNotMatchingBlocks(text, blocks);
            blocks.Sort();
            return blocks;
        }

        private void appendNotMatchingBlocks(string text, List<TextBlock> blocks)
        {
            int blockCount = blocks.Count;
            if (blocks[0].index > 0)
            {
                blocks.Add(new TextBlock(false, 0, blocks[0].index, text.Substring(0, blocks[0].index)));
            }
            TextBlock prevBlock = blocks[0];
            for (int i = 1; i < blockCount; i++)
            {
                int prevEndIndex = prevBlock.index + prevBlock.length;
                int length = blocks[i].index - prevEndIndex;
                if (prevEndIndex < blocks[i].index)
                {
                    blocks.Add(new TextBlock(false, prevEndIndex, length, text.Substring(prevEndIndex, length)));
                }
                prevBlock = blocks[i];
            }
            if (prevBlock.index + prevBlock.length < text.Length)
            {
                int prevEndIndex = prevBlock.index + prevBlock.length;
                int length = text.Length - prevEndIndex;
                blocks.Add(new TextBlock(false, prevBlock.index + prevBlock.length, length, text.Substring(prevEndIndex, length)));
            }
        }
    }

    class CommentAndStringProcessor : TextProcessorDecorator
    {
        private static char quote = "'"[0];

        public CommentAndStringProcessor(ITextProcessor textProcessor) : base(textProcessor)
        {
        }

        protected override string ProcessText(string text)
        {            
            return $"<b style='color:{getBlockColor(text)}'>{text}</b>";
        }

        static string getBlockColor(string text)
        {            
            return text[0] == quote ? "red" : "green";
        }

        protected override List<TextBlock> SplitText(string text)
        {
            var quotes = Regex.Matches(text, "'", RegexOptions.Multiline, new TimeSpan(0, 0, 1));
            var openComments = Regex.Matches(text, @"/\*", RegexOptions.Multiline, new TimeSpan(0, 0, 1));
            var closeComments = Regex.Matches(text, @"\*/", RegexOptions.Multiline, new TimeSpan(0, 0, 1));
            var list = quotes.Cast<Match>().Concat(openComments.Cast<Match>()).Concat(closeComments.Cast<Match>()).ToList();
            List<TextBlock> result = new List<TextBlock>();
            if (list.Count == 0)
            {
                result.Add(new TextBlock(false, 0, text.Length, text));
                return result;
            }
            list.Sort((a, b) => a.Index == b.Index ? 0 : a.Index > b.Index ? 1 : -1);
            int index = 0;
            int pos = 0;

            while (index < list.Count)
            {
                if (list[index].Value == "/*")
                {
                    if (isInSingleLineComment(text, list[index].Index))
                    {
                        index++;
                        continue;
                    }
                    result.Add(new TextBlock(false, pos, list[index].Index - pos, text.Substring(pos, list[index].Index - pos)));
                    pos = list[index].Index;
                    while (list[index++].Value != "*/") ;
                    result.Add(new TextBlock(true, pos, list[index - 1].Index - pos, text.Substring(pos, list[index - 1].Index - pos + list[index - 1].Length)));
                    pos = list[index - 1].Index + list[index - 1].Length;
                }

                if (index < list.Count && list[index].Value == "'")
                {
                    if (isInSingleLineComment(text, list[index].Index))
                    {
                        index++;
                        continue;
                    }
                    result.Add(new TextBlock(false, pos, list[index].Index - pos, text.Substring(pos, list[index].Index - pos)));
                    pos = list[index].Index;
                    index++;
                    while (list[index++].Value != "'") ;
                    result.Add(new TextBlock(true, pos, list[index - 1].Index - pos, text.Substring(pos, list[index - 1].Index - pos + list[index - 1].Length)));
                    pos = list[index - 1].Index + list[index - 1].Length;
                }
            }
            if (result.Count > 0)
            {
                pos = list[index - 1].Index + list[index - 1].Length;
            }
            result.Add(new TextBlock(false, pos, text.Length - pos, text.Substring(pos, text.Length - pos)));
            return result;
        }

        private bool isInSingleLineComment(string text, int ix)
        {
            while (true)
            {
                if (text[ix] == '\n' || ix == 0)
                {
                    return false;
                }
                if (text[ix] == '-' && text[ix - 1] == '-')
                {
                    return true;
                }
                ix--;
            }
        }
    }

    class TextBlock : IComparable<TextBlock>
    {
        public TextBlock(bool isMatch, int index, int length, string text)
        {
            this.isMatch = isMatch;
            this.index = index;
            this.length = length;
            this.text = text;
        }

        public int CompareTo(TextBlock other)
        {
            if (this.index == other.index)
            {
                return 0;
            }
            if (this.index > other.index)
            {
                return 1;
            }
            return -1;
        }

        public override string ToString()
        {
            return text;
        }

        public bool isMatch;
        public int index;
        public int length;
        public string text;
    }
}
