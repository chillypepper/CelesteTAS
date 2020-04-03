using CelesteStudio.Controls;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace CelesteStudio.Entities
{
    public class FileVerifier
    {
        // This is the textbox shown in the studio window - it's the only reliable source of line data
        private RichText tasText;

        // These keep track of the lines and metadata of our controlled text for pushing out to tasText
        private List<InputRecord> tasLines = null;
        private int totalFrames = 0;
        private string previousTime = "";

        // These are output strings for the file verification
        private string sectionStartTimeMarker = "SectionStart";
        private string fileEndTimeMarker = "FileEnd";
        private string verifiedTimeMarker = "VerifiedTimestamp";

        public FileVerifier(RichText tasText)
        {
            this.tasText = tasText;
        }

        // This will begin listening for updates to record timestamps for the file, as well as run the first formatting pass
        public void BeginVerifyingFile()
        {
            UpdateFileLines();
            UpdateFileText(false);
        }

        // This will finalise the current file, running the final clean (leaving in timestamps) and then saving the content
        public void EndVerifyingFile()
        {
            UpdateFileText(true);
            tasLines = null;
        }

        // This is the main update loop, we'll update line data with timestamps here if the verification is running
        public void UpdateValues(GameMemory memory)
        {
            if (tasLines != null)
            {
                // TASOutput is the current frame data and TASPlayerOutput is the wall of text at the bottom of the window
                string tasOutput = memory.TASOutput();
                string tasPlayerOutput = memory.TASPlayerOutput();

                if (!string.IsNullOrWhiteSpace(tasOutput) && !string.IsNullOrWhiteSpace(tasPlayerOutput))
                {
                    // TASOutput format: <1_BASED_CURRENT_LINE>[<LINE_TEXT>(<CURRENT_LINE_FRAME> / <MAX_LINE_FRAME> : <CURRENT_FRAME>)]
                    int[] indexes = new int[] { -1, tasOutput.IndexOf('['), tasOutput.IndexOf('('), tasOutput.IndexOf('/'), tasOutput.IndexOf(':'), tasOutput.IndexOf(')') };
                    int currentLine = int.Parse(tasOutput.Substring(indexes[0] + 1, indexes[1] - indexes[0] - 1).Trim()) - 1;
                    string lineText = tasOutput.Substring(indexes[1] + 1, indexes[2] - indexes[1] - 1).Trim();
                    string currentLineFrame = tasOutput.Substring(indexes[2] + 1, indexes[3] - indexes[2] - 1).Trim();
                    string maxLineFrame = tasOutput.Substring(indexes[3] + 1, indexes[4] - indexes[3] - 1).Trim();
                    string currentFrame = tasOutput.Substring(indexes[4] + 1, indexes[5] - indexes[4] - 1).Trim();

                    // Just regex what we need from the block - current time is marked by "Timer: (<TIME>)"
                    string time = Regex.Match(tasPlayerOutput, @"Timer: (\d+\.\d+)").Groups[1].ToString();

                    // If we're on the last frame of the current line but not just at the start of the file, check if we're ready to mark time. Reset timer on frame 1
                    if (currentFrame == "1")
                    {
                        time = "0.000";
                    }
                    else if (currentFrame == totalFrames.ToString())
                    {
                        // If we're on the last line modify the first and last lines with the total time verification
                        string verificationLine = string.Join("", "#", GetMarkerOutput(fileEndTimeMarker, string.IsNullOrWhiteSpace(time) ? previousTime : time), " verified at ", GetMarkerOutput(verifiedTimeMarker, string.Format("{0:s}", DateTime.UtcNow)));

                        tasLines[0].Notes = verificationLine;
                        tasLines[tasLines.Count - 1].Notes = verificationLine;
                    }
                    else if (currentLineFrame == "0" || (!string.IsNullOrWhiteSpace(time) && time != previousTime && currentLineFrame == "1"))
                    {
                        // currentLineFrame 0 is a special case - when coming from a level exit at the start of a file, it hangs for a while as frame 0, so make sure it only happens once
                        if (currentLineFrame == "0")
                        {
                            if (previousTime == "currentLineFrame0")
                            {
                                return;
                            }

                            time = "0.000";
                        }

                        InputRecord previousLine = currentLine - 1 > 0 ? tasLines[currentLine - 1] : null;

                        // If the previous line is a comment or a break then add the timestamp to the last comment found
                        // TODO How should this work if there's just a break in the middle of a section? Just append multiple for now
                        if (previousLine.Frames == 0)
                        {
                            for (int i = currentLine - 1; i != 0; --i)
                            {
                                if (tasLines[i].Frames == 0 && !tasLines[i].FastForward && !string.IsNullOrWhiteSpace(tasLines[i].Notes))
                                {
                                    tasLines[i].Notes = string.Join(" ", new string[] { tasLines[i].Notes, GetMarkerOutput(sectionStartTimeMarker, time) });

                                    break;
                                }
                            }
                        }

                        // currentLineFrame 0 is a special case - when coming from a level exit at the start of a file, it hangs for a while as frame 0, so make sure it only happens once
                        if (currentLineFrame == "0")
                        {
                            time = "currentLineFrame0";
                        }
                    }

                    // Update the time to prevent multiple runs of the same content
                    previousTime = string.IsNullOrWhiteSpace(time) ? previousTime : time;
                }
            }
        }

        // Get a marker string for output into the file
        private string GetMarkerOutput(string marker, string value)
        {
            return string.Join("", new string[] { "<", marker, "=", value, ">" });
        }

        private void UpdateFileText(bool isFinalOutput)
        {
            if (tasLines != null)
            {
                List<string> tasStrings = new List<string>();

                // Build the final string from the new commands
                for (int i = 0; i != tasLines.Count; ++i)
                {
                    if (!isFinalOutput || !tasLines[i].FastForward)
                    {
                        tasStrings.Add(tasLines[i].ToString());
                    }
                }

                // Replace all existing text content, and add one fast forward to the end (removed on final publish)
                tasText.SelectAll();
                tasText.InsertText(string.Join(System.Environment.NewLine, tasStrings).TrimEnd() + System.Environment.NewLine);
            }
        }

        // This is responsible for gathering and formatting the current file from the tasText textbox then updating the lines and the metadata for the verifier
        private void UpdateFileLines()
        {
            tasLines = new List<InputRecord>(tasText.Lines.Count);
            totalFrames = 0;
            previousTime = "0.000";

            // Start the file with one blank line, this will be filled out with the final verification at the end
            tasLines.Add(new InputRecord(string.Empty));

            for (int i = 0; i != tasText.Lines.Count; ++i)
            {
                InputRecord currentLine = new InputRecord(tasText.Lines[i]);
                InputRecord lastLine = tasLines[tasLines.Count - 1];

                // Don't keep any fast forward lines
                if (currentLine.FastForward)
                {
                    continue;
                }

                // 0 frame lines are anything without direct in game actions - reads, comments, etc
                if (currentLine.Frames == 0)
                {
                    // Allow empty lines only if there aren't multiple in a row
                    if (string.IsNullOrWhiteSpace(currentLine.Notes))
                    {
                        if (!string.IsNullOrWhiteSpace(lastLine.ToString()))
                        {
                            // Add the blank line and a breakpoint for easy skipping
                            tasLines.Add(new InputRecord("***!"));
                            tasLines.Add(new InputRecord(string.Empty));
                        }
                    }
                    else
                    {
                        // If the line has content then clean it, check it's still worth keeping (not an old verification, blank, or 0s), then save it if needed
                        string trimmedNotes = currentLine.Notes.Trim();

                        if (!string.IsNullOrWhiteSpace(trimmedNotes) &&
                            !Regex.IsMatch(trimmedNotes, @"^#" + GetMarkerOutput(fileEndTimeMarker, ".*?")) &&
                            !Regex.IsMatch(trimmedNotes, @"^0+$")
                        )
                        {
                            string content = Regex.Replace(currentLine.Notes, @"\" + GetMarkerOutput(sectionStartTimeMarker, ".*?"), "").TrimEnd();

                            if (!string.IsNullOrWhiteSpace(content))
                            {
                                // Add the comment and a breakpoint for easy skipping
                                tasLines.Add(new InputRecord("***!"));
                                tasLines.Add(new InputRecord(content));
                            }
                        }
                    }
                }
                else
                {
                    // If we're on an action line then add it if it's new, or update the previous line if it's a duplicate
                    totalFrames += currentLine.Frames;

                    if (lastLine.Frames != 0 &&
                        currentLine.ActionsToString() == lastLine.ActionsToString() &&
                        (!currentLine.HasActions(Actions.Feather) || currentLine.Angle == lastLine.Angle)
                    )
                    {
                        lastLine.Frames += currentLine.Frames;
                    }
                    else if (currentLine.HasActions(Actions.Feather))
                    {
                        tasLines.Add(new InputRecord(currentLine.Frames + ",F," + currentLine.Angle));
                    }
                    else
                    {
                        tasLines.Add(new InputRecord(currentLine.Frames, currentLine.Actions));
                    }
                }
            }

            // End the file with one blank line, this will be filled out with the final verification at the end
            if (tasLines[tasLines.Count - 1].Frames != 0 || !string.IsNullOrWhiteSpace(tasLines[tasLines.Count - 1].Notes))
            {
                tasLines.Add(new InputRecord(string.Empty));
            }
        }
    }
}
