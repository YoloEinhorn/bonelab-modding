﻿using System.Text.RegularExpressions;
using GitCommands;
using GitExtUtils;
using GitUIPluginInterfaces;

namespace GitUI.CommandsDialogs
{
    partial class FormVerify
    {
        private enum LostObjectType
        {
            Commit,
            Blob,
            Tree,
            Tag,
            Other
        }

        private sealed partial class LostObject
        {
            [GeneratedRegex(@"^((dangling|missing|unreachable) (commit|blob|tree|tag)|warning in tree) ([a-f\d]{40})(.)*$")]
            private static partial Regex RawDataRegex();
            [GeneratedRegex("^(?<author>[^\u001F]+)\u001F(?<subject>.*)\u001F(?<date>\\d+)\u001F(?<first_parent>[^ ]+)?( .+)?$", RegexOptions.Singleline)]
            private static partial Regex LogRegex();
            [GeneratedRegex(@"^object (.+)\ntype commit\ntag (.+)\ntagger (.+) <.*> (.+) .*\n\n(.*)\n", RegexOptions.Multiline)]
            private static partial Regex TagRegex();

            /// <summary>
            /// {0} - lost object's hash.
            /// %aN - committer name.
            /// %s  - subject.
            /// %ct - committer date, UNIX timestamp (easy to parse format).
            /// </summary>
            private static readonly string LogCommandArgumentsFormat = (ArgumentString)new GitArgumentBuilder("log")
            {
                "-n1",
                "--pretty=format:\"%aN\u001F%s\u001F%ct\u001F%P\" {0}"
            };

            private static readonly string TagCommandArgumentsFormat = (ArgumentString)new GitArgumentBuilder("cat-file")
            {
                "-p",
                "{0}"
            };

            public LostObjectType ObjectType { get; }

            /// <summary>
            /// Id (SHA-1 hash) of the lost object.
            /// </summary>
            public ObjectId ObjectId { get; }

            /// <summary>
            /// Id (SHA-1 hash) of parent commit to the lost object.
            /// </summary>
            public ObjectId? Parent { get; private set; }

            /// <summary>
            /// Diagnostics and object type.
            /// </summary>
            public string RawType { get; }

            public string? Author { get; private set; }
            public string? Subject { get; private set; }
            public DateTime? Date { get; private set; }

            /// <summary>
            /// Tag name (for a tag object).
            /// </summary>
            public string? TagName { get; set; }

            private LostObject(LostObjectType objectType, string rawType, ObjectId objectId)
            {
                // TODO use enum for RawType
                ObjectType = objectType;
                RawType = rawType;
                ObjectId = objectId ?? throw new ArgumentNullException(nameof(objectId));
            }

            public static LostObject? TryParse(IGitModule module, string raw)
            {
                if (string.IsNullOrEmpty(raw))
                {
                    throw new ArgumentException("Raw source must be non-empty string", raw);
                }

                Match patternMatch = RawDataRegex().Match(raw);

                // show failed assertion for unsupported cases (for developers)
                // if you get this message,
                //     you can implement this format parsing
                //     or post an issue to https://github.com/gitextensions/gitextensions/issues
                DebugHelpers.Assert(patternMatch.Success, "Lost object's extracted diagnostics format not implemented");

                // skip unsupported raw data format (for end users)
                if (!patternMatch.Success)
                {
                    return null;
                }

                GroupCollection matchedGroups = patternMatch.Groups;
                string rawType = matchedGroups[1].Value;
                LostObjectType objectType = GetObjectType(matchedGroups[3]);
                ObjectId objectId = ObjectId.Parse(raw, matchedGroups[4]);
                LostObject result = new(objectType, rawType, objectId);

                if (objectType == LostObjectType.Commit)
                {
                    string commitLog = GetLostCommitLog();
                    Match logPatternMatch = LogRegex().Match(commitLog);
                    if (logPatternMatch.Success)
                    {
                        result.Author = module.ReEncodeStringFromLossless(logPatternMatch.Groups["author"].Value);
                        result.Subject = module.ReEncodeCommitMessage(logPatternMatch.Groups["subject"].Value) ?? "";
                        result.Date = DateTimeUtils.ParseUnixTime(logPatternMatch.Groups["date"].Value);
                        string firstParent = logPatternMatch.Groups["first_parent"].Value;
                        if (!string.IsNullOrEmpty(firstParent))
                        {
                            result.Parent = ObjectId.Parse(firstParent);
                        }
                    }
                }
                else if (objectType == LostObjectType.Tag)
                {
                    string tagData = GetLostTagData();
                    Match tagPatternMatch = TagRegex().Match(tagData);
                    if (tagPatternMatch.Success)
                    {
                        result.Parent = ObjectId.Parse(tagData, tagPatternMatch.Groups[1]);
                        result.Author = module.ReEncodeStringFromLossless(tagPatternMatch.Groups[3].Value);
                        result.TagName = tagPatternMatch.Groups[2].Value;
                        result.Subject = result.TagName + ":" + tagPatternMatch.Groups[5].Value;
                        result.Date = DateTimeUtils.ParseUnixTime(tagPatternMatch.Groups[4].Value);
                    }
                }
                else if (objectType == LostObjectType.Blob)
                {
                    string hash = objectId.ToString();
                    string blobPath = Path.Combine(module.WorkingDirGitDir, "objects", hash[..2], hash[2..ObjectId.Sha1CharCount]);
                    result.Date = new FileInfo(blobPath).CreationTime;
                }

                return result;

                string GetLostCommitLog() => VerifyHashAndRunCommand(LogCommandArgumentsFormat);
                string GetLostTagData() => VerifyHashAndRunCommand(TagCommandArgumentsFormat);

                string VerifyHashAndRunCommand(ArgumentString commandFormat)
                {
                    return module.GitExecutable.GetOutput(string.Format(commandFormat, objectId), outputEncoding: GitModule.LosslessEncoding);
                }

                LostObjectType GetObjectType(Group matchedGroup)
                {
                    if (!matchedGroup.Success)
                    {
                        return LostObjectType.Other;
                    }

                    return matchedGroup.Value switch
                    {
                        "commit" => LostObjectType.Commit,
                        "blob" => LostObjectType.Blob,
                        "tree" => LostObjectType.Tree,
                        "tag" => LostObjectType.Tag,
                        _ => LostObjectType.Other
                    };
                }
            }
        }
    }
}
