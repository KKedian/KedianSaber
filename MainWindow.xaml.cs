using System;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.ComponentModel;
using Newtonsoft.Json;

namespace KedianSaber
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        /*PLOTTABLES*/
        ScottPlot.Plottable.ScatterPlot rankedMaps; //Array that will hold the plotting data of the ranked scores. Created here to provide sufficient scoping for plot events. The same goes for the playerScores array.
        ScottPlot.Plottable.MarkerPlot tempPoint; //A single point in the plot for highlighting another point.
        double[] percents;
        double[] stars;

        /*PLAYER INFO, SCORES AND MISC*/
        Player newPlayer;
        PlayerScore[] playerScores; //Array to hold objects of type playerScore. This will hold every score that the player has made. As such, its size is initialised once the number of ranked plays set by the player is known.
        double lowestAccPercent; //Will store the lowest accuracy percentage of all maps. This will be used to optimally build the graph. Declared here to give sufficient scoping that it is visible to the mouse move event of the plot.
        double highestPageNeeded;

        /*DOWNLOADING AND PROCESSING*/
        string ID; //ID of the target scoresaber account.
        string URL; //URL of scoresaber API data.
        byte[] raw; //Array of bytes for storing downloaded raw data.
        string webData; //The result of decoding the web data into a UTF-8 string.
        string[] current8Songs = new string[8]; //Array to store the json data for each of the individual scores from a full page of scores.
        readonly System.Net.WebClient wc = new System.Net.WebClient(); //Web client to download json data from scoresaber.

        public MainWindow()
        {
            InitializeComponent();

            /*SET UP GRAPH*/
            plot1.Plot.YLabel("Score Percentage");
            plot1.Plot.XLabel("Stars");

            plot1.Plot.Style(ScottPlot.Style.Blue1);

            plot1.Configuration.Zoom = false; //Prevents the axis' ranges from being changed by the drag or scroll.
            plot1.Configuration.Pan = false;

            plot1.Plot.YAxis.TickLabelFormat("P1", dateTimeFormat: false); //Formats tick labels as percentages (1 = 100%, 0.6 = 60%, etc.).
            plot1.Plot.XAxis.ManualTickSpacing(1); //1 tick per 1 star

            plot1.Plot.SetAxisLimitsY(0, 1); //1 indicates proportion of score percent. Percentage tick labeling mandates this solution.
            plot1.Plot.SetAxisLimitsX(0, 13); //13 is the max star rounded to positive infinity.

            //ADD A BIG RAT MODE HERE, FOLLOWING CODE MAKES GRAPH TRANSPARENT
            //plot1.Plot.Style(figureBackground: System.Drawing.Color.Transparent, dataBackground: System.Drawing.Color.Transparent);
        }

        private void submitidbutton_Click(object sender, RoutedEventArgs e)
        {
            /*COLLECT PLAYER STATS*/
            progressBar.Visibility = Visibility.Visible;
            progressText.Visibility = Visibility.Visible;

            bigrat.Visibility = Visibility.Visible;
            bigratlabel.Visibility = Visibility.Visible;

            ID = idbox.Text;

            //Links to a player's full scoresaber data.
            URL = "https://scoresaber.com/api/player/" + ID + "/full";

            //Downloads raw data from scoresaber.
            raw = wc.DownloadData(URL);

            //Converts raw data into a string of json data.
            webData = Encoding.UTF8.GetString(raw);

            //Deserializes json into a player object.
            newPlayer = JsonConvert.DeserializeObject<Player>(webData);

            progressText.Text = "Progress: 0/" + newPlayer.ScoreStats.RankedPlayCount;

            //Rounds the player's average ranked accuracy to 2 decimal places. Otherwise, it is far too precise.
            newPlayer.ScoreStats.AverageRankedAccuracy = Math.Round(newPlayer.ScoreStats.AverageRankedAccuracy, 2);

            //Get the max page number needed using their ranked play count / 8 (rounded toward infinity).
            highestPageNeeded = Math.Round((double)newPlayer.ScoreStats.RankedPlayCount / 8, MidpointRounding.ToPositiveInfinity);

            //Initialises the array of scores and the arrays for their percentages and stars. The max size is determined by the number of scores that will be collected.
            //stars and percents arrays only need to store the values for ranked plays, playScores will contain loved plays.
            playerScores = new PlayerScore[(int)highestPageNeeded * 8];
            percents = new double[newPlayer.ScoreStats.RankedPlayCount];
            stars = new double[newPlayer.ScoreStats.RankedPlayCount];

            /*CREATES BACKGROUNDWORKER TO GET SCORES IN BACKGROUND, ALLOWING UI TO RUN ASYNCHRONIOUSLY*/
            BackgroundWorker worker = new BackgroundWorker{WorkerReportsProgress = true};
            worker.DoWork += Worker_DoWork;
            worker.ProgressChanged += Worker_ProgressChanged;
            worker.RunWorkerCompleted += Worker_RunWorkerCompleted;
            worker.RunWorkerAsync();
        }

        private void Worker_DoWork(object sender, DoWorkEventArgs e) //Done in background once RunWorkerAsync() called, lets UI run at same time.
        {
            /*COLLECT EACH PAGE'S SCORE STATS UNTIL LAST PAGE WITH RANKED SCORES*/
            for (int i = 0; i < highestPageNeeded; i++)
            {
                //Links to a page containing 8 of a player's scores. Each loop increments the page to get.
                URL = "https://scoresaber.com/api/player/" + ID + "/scores?sort=top&page=" + (i + 1) + "&withMetadata=false";

                try
                {
                    raw = wc.DownloadData(URL);
                }
                catch (Exception exc)
                {
                    Console.WriteLine("Error downloading page " + (i+1) + ": " + exc);
                }

                webData = Encoding.UTF8.GetString(raw);

                //Creates a new array to store score json data for each of the 8 scores on a page.
                current8Songs = SplitPageOfScores(webData);

                for (int x = 0; x < 8; x++)
                {
                    playerScores[(i * 8) + x] = JsonConvert.DeserializeObject<PlayerScore>(current8Songs[x]);
                    playerScores[(i * 8) + x].CalcScorePercent();

                    if (playerScores[(i * 8) + x].Leaderboard.Ranked)
                    {
                        percents[(i * 8) + x] = playerScores[(i * 8) + x].ScorePercent / 100;
                        stars[(i * 8) + x] = playerScores[(i * 8) + x].Leaderboard.Stars;
                    }
                    double progressPercent = (((((double)i * 8) + x) + 1) / newPlayer.ScoreStats.RankedPlayCount) * 100;
                    (sender as BackgroundWorker).ReportProgress(Convert.ToInt32(progressPercent), ((i * 8) + x) + 1);
                    System.Threading.Thread.Sleep(1);
                }
            }
        }

        private void Worker_ProgressChanged(object sender, ProgressChangedEventArgs e) //Progress updated each time a score is collected.
        {
            progressBar.Value = e.ProgressPercentage;
            if ((int)e.UserState <= newPlayer.ScoreStats.RankedPlayCount)
            {
                progressText.Text = "Progress: " + e.UserState + "/" + newPlayer.ScoreStats.RankedPlayCount;
            }
        }

        private void Worker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e) //Performed once all scores are collected.
        {
            /*PLOT SCORES AND SET UP GRAPH*/
            plot1.Plot.Title(newPlayer.Name + "'s plays");

            rankedMaps = plot1.Plot.AddScatterPoints(stars, percents);

            plot1.Plot.Add(rankedMaps);

            lowestAccPercent = percents.Min();

            lowestAccPercent = Math.Round(lowestAccPercent * 10, MidpointRounding.ToNegativeInfinity) / 10; //Lowest acc percent, rounded down toward lowest 10.

            if (lowestAccPercent >= 0.9) //If the graph needs to display an accuracy range of 90-100% scores or less, have ticks at each 1%, 70-100% is every 5%, otherwise at each 10%.
            {
                plot1.Plot.YAxis.ManualTickSpacing(0.01);
            }
            else if (lowestAccPercent >= 0.7)
            {
                plot1.Plot.YAxis.ManualTickSpacing(0.05);
            }
            else
            {
                plot1.Plot.YAxis.ManualTickSpacing(0.1);
            }

            plot1.Plot.SetAxisLimitsY(lowestAccPercent, 1);

            plot1.Refresh();

            /*SET CORRECT PLAYER DATA*/
            playerNameLabel.Content = newPlayer.Name;
            playerRanksLabel.Content = "#" + newPlayer.Rank + " Global - #" + newPlayer.CountryRank + " " + newPlayer.Country;
            playerPPLabel.Content = newPlayer.PP + "pp";
            playerRankedStats.Content = newPlayer.ScoreStats.AverageRankedAccuracy + "% - Ranked plays: " + newPlayer.ScoreStats.RankedPlayCount;
            int HMD = playerScores[0].Score.HMD;
            string[] hmds = new string[9] { "Oculus Rift CV1", "HTC Vive", "HTC Vive Pro", "Windows Mixed Reality", "Oculus Rift S", "Oculus Quest", "Valve Index", "HTC Vive Cosmos" , "Unknown HMD" }; //Unknown hmd is '0' in the JSON data.
            
            int tempIndex = HMD switch
            {
                1 => 0,
                2 => 1,
                4 => 2,
                8 => 3,
                16 => 4,
                32 => 5,
                64 => 6,
                128 => 7,
                _ => 8,
            }; //THESE ARE ALL BASE 2 LOGARITHMS OF THEIR COUNTERPARTS, INVESTIGATE MORE EFFICIENT SOLUTION.

            playerHMDLabel.Content = hmds[tempIndex];

            playerProfileImage.Source = new BitmapImage(new Uri(newPlayer.ProfilePicture));

            /*SET CORRECT VISIBILITIES*/
            AccountLinkPage.Visibility = Visibility.Collapsed;
            GraphPage.Visibility = Visibility.Visible;
        }

        public string[] SplitPageOfScores(string APIReturn)
        {
            //Splits the raw json data by each newline character. Specific lines are taken to easily create useful json data for each individual score. As each API return contains the data for 8 scores, a string array of size 8 is used to return the individual scores to the main program.
            string[] scoresData = new string[8];
            string[] individualLines = APIReturn.Split('\n');

            scoresData[0] = String.Join('\n', individualLines, 2, 49).TrimEnd(',');
            scoresData[1] = String.Join('\n', individualLines, 51, 49).TrimEnd(',');
            scoresData[2] = String.Join('\n', individualLines, 100, 49).TrimEnd(',');
            scoresData[3] = String.Join('\n', individualLines, 149, 49).TrimEnd(',');
            scoresData[4] = String.Join('\n', individualLines, 198, 49).TrimEnd(',');
            scoresData[5] = String.Join('\n', individualLines, 247, 49).TrimEnd(',');
            scoresData[6] = String.Join('\n', individualLines, 296, 49).TrimEnd(',');
            scoresData[7] = String.Join('\n', individualLines, 345, 49).TrimEnd(',');

            return scoresData;
        }

        private void plot1_MouseMove(object sender, MouseEventArgs e)
        {
            try
            {
                (double mouseCoordX, double mouseCoordY) = plot1.GetMouseCoordinates();
                double xyRatio = plot1.Plot.XAxis.Dims.PxPerUnit / plot1.Plot.YAxis.Dims.PxPerUnit;
                (double pointX, double pointY, int pointIndex) = rankedMaps.GetPointNearest(mouseCoordX, mouseCoordY, xyRatio);

                if (CloseEnoughToCursor(pointX, pointY, xyRatio, mouseCoordX, mouseCoordY))
                {
                    //Remove the previous temp point, create a new point with the appropriate location on the plot and refresh the plot to make it appear.
                    plot1.Plot.Remove(tempPoint);
                    tempPoint = plot1.Plot.AddPoint(pointX, pointY, System.Drawing.Color.Red, shape: ScottPlot.MarkerShape.openCircle, size: 7);
                    plot1.Refresh();

                    //Update song label content to include data of map point closest to cursor.
                    songNameLabel.Content = playerScores[pointIndex].Leaderboard.SongName + " " + playerScores[pointIndex].Leaderboard.SongSubName;
                    artistMapperNameLabel.Content = "Song by " + playerScores[pointIndex].Leaderboard.SongAuthorName + " - Mapped by " + playerScores[pointIndex].Leaderboard.LevelAuthorName;
                    plottedDataLabel.Content = playerScores[pointIndex].ScorePercent + "% - " + playerScores[pointIndex].Leaderboard.Stars + " star";
                    scorePPLabel.Content = playerScores[pointIndex].Score.PP + "pp";
                    mapHashLabel.Content = playerScores[pointIndex].Leaderboard.SongHash;
                    mapCoverImage.Source = new BitmapImage(new Uri(playerScores[pointIndex].Leaderboard.CoverImage));
                }
            }
            catch (NullReferenceException)
            {

            }
        }

        public bool CloseEnoughToCursor(double pointXPos, double pointYPos, double xyRatio, double mouseXPos, double mouseYPos)
        {
            double distance = Math.Sqrt((Math.Pow(pointXPos - mouseXPos, 2) * xyRatio) + (Math.Pow(pointYPos - mouseYPos, 2) / xyRatio));

            if (distance <= 0.03)
            {
                return true;
            }
            return false;
        }
    }

    public class Player
    {
        public string Name { get; set; }
        public string ProfilePicture { get; set; }
        public string Country { get; set; }
        public double PP { get; set; }
        public int Rank { get; set; }
        public int CountryRank { get; set; }
        public Stats ScoreStats { get; set; }
    }

    public class Stats
    {
        public long TotalScore { get; set; }
        public long TotalRankedScore { get; set; }
        public double AverageRankedAccuracy { get; set; }
        public int TotalPlayCount { get; set; }
        public int RankedPlayCount { get; set; }
        public int ReplaysWatched { get; set; }
    }

    public class PlayerScore
    {
        public Score Score { get; set; }
        public Leaderboard Leaderboard { get; set; }
        public double ScorePercent { get; set; }

        public void CalcScorePercent()
        {
            try
            {
                ScorePercent = Math.Round(((double)this.Score.BaseScore / this.Leaderboard.MaxScore) * 100, 2);
            }
            catch (DivideByZeroException)
            {
                Console.WriteLine("Cannot divide by 0.");
            }
        }
    }

    public class Score
    {
        public int ID { get; set; }
        public int Rank { get; set; }
        public long BaseScore { get; set; }
        public long ModifiedScore { get; set; }
        public double PP { get; set; }
        public double Weight { get; set; }
        public string Modifiers { get; set; }
        public double Multiplier { get; set; }
        public int BadCuts { get; set; }
        public int MissedNotes { get; set; }
        public int MaxCombo { get; set; }
        public bool FullCombo { get; set; }
        public int HMD { get; set; }
        public string TimeSet { get; set; }
        public bool HasReplay { get; set; }
    }

    public class Leaderboard
    {
        public int ID { get; set; }
        public string SongHash { get; set; }
        public string SongName { get; set; }
        public string SongSubName { get; set; }
        public string SongAuthorName { get; set; }
        public string LevelAuthorName { get; set; }
        public Difficulty Difficulty { get; set; }
        public long MaxScore { get; set; }
        public string CreatedDate { get; set; }
        public string RankedDate { get; set; }
        public string QualifiedDate { get; set; }
        public string LovedDate { get; set; }
        public bool Ranked { get; set; }
        public bool Qualified { get; set; }
        public bool Loved { get; set; }
        public double Stars { get; set; }
        public bool PositiveModifiers { get; set; }
        public string CoverImage { get; set; }
    }

    public class Difficulty
    {
        public int LeaderboardId { get; set; }
        public int Diff { get; set; }
        public string GameMode { get; set; }
        public string DifficultyRaw { get; set; }
    }
}
