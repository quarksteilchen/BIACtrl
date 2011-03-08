using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Net;
using System.IO;
using System.Collections;
using System.Threading;

namespace BIActrl
{
    public partial class Form1 : Form
    {
        #region Member Variables
        public int curX;
        public int curY;
        public int curZoom;
        public char curDir; // u,d,l,r - Up Down Left Right
        private int dirFx; // direction front (+1, -1, 0)
        private int dirFy; // direction front (+1, -1, 0)
        private int dirSx; // direction side X
        private int dirSy; // direction side Y
        private bool isOnLine;
        private bool prevTileHiR;
        public bool cancelAll;

        private string entryTile;
        //private char entryDir;

        public int numberOfTiles;
        public int numberOfTilesMAX = 10000;
        private bool leavedEntry = true;
        PictureBox pbFront;
        PictureBox pbSide;

        public Dictionary<int,List<String>> history;

        #endregion



        public Form1()
        {
            this.cancelAll = false;
            this.curZoom = 14;
            this.curX = 0;
            this.curY = 0;

            this.isOnLine = false;
            this.prevTileHiR = false;

            // default direction is left
            this.dirFx = -1;
            this.dirFy = 0;
            this.curDir = 'l';
            this.dirSx = 0;
            this.dirSy = 1;

            //this.entryDir = '\0';
            this.entryTile = String.Empty;

            this.numberOfTiles = 0;

            InitializeComponent();

            this.buttonClr_Click(null, EventArgs.Empty);

            this.pbFront = this.pictureBox6;
            this.pbSide = this.pictureBox8;

            this.Text = Application.ProductName + " " + Application.ProductVersion + "    BETA";
            this.comboBoxZoom.SelectedIndex = this.curZoom-1; // 14
        }

        private void buttonGo_Click(object sender, EventArgs e)
        {
            // we want to calculate X/Y tile where to start, so first look, which information the user has entered.
            this.parsePositions(true);

            this.numberOfTiles = 0;
            this.numberOfTilesMAX = 10000;
            //this.entryDir = 'l';
            this.curDir = 'l';
            this.entryTile = String.Empty;

            this.cancelAll = false;

            switch (this.curDir)
            {
                case 'u': this.labelDirection.Text = "^"; break;
                case 'r': this.labelDirection.Text = ">"; break;
                case 'd': this.labelDirection.Text = "v"; break;
                case 'l': this.labelDirection.Text = "<"; break;
                default:
                    break;
            }

            byte[] raw;
            this.pictureBox1.Image = this.getPic(this.curX, this.curY, out raw);


            for (int i = 0; i < numberOfTilesMAX; i++)
            {
                if (this.cancelAll == false)
                {
                    decideNext();
                }
                else
                {
                    this.labelRec.Text = "Cancelled";
                    break;
                }
            }
        }

        public void parsePositions(bool setStatus)
        {

            if (this.textBoxX.Text.Length > 0 && this.textBoxY.Text.Length > 0)
            {
                // user has entered X/Y tile coordinate
                try
                {
                    this.curX = Int32.Parse(this.textBoxX.Text);
                    this.curY = Int32.Parse(this.textBoxY.Text);
                }
                catch
                {
                    MessageBox.Show("Wrong Format on X/Y Tile.");
                }
            }
            else if (this.textBoxTile.Text.Length > 0)
            {
                // user has entered a tile number(-address) aka QuadKey
                try
                {
                    TileSystem.QuadKeyToTileXY(this.textBoxTile.Text, out this.curX, out this.curY, out this.curZoom);
                }
                catch
                {
                    MessageBox.Show("Wrong Format on Tilenumber (QuadKey)");
                    return;
                }
            }
            else if (this.textBoxLatN.Text.Length > 0 && this.textBoxLonE.Text.Length > 0)
            {
                // user has entered lat/lon combination
                try
                {
                    TileSystem.LatLongToPixelXY(Double.Parse(this.textBoxLatN.Text), Double.Parse(this.textBoxLonE.Text), this.curZoom, out this.curX, out this.curY);
                    // NOTE: currently this.curX and this.curY holds pixel coordinates, not tile coordinates!
                    this.curX = this.curX / 256;
                    this.curY = this.curY / 256;
                }
                catch
                {
                    MessageBox.Show("Wrong Format on Lat/Lon.");
                    return;
                }
            }

            this.curZoom = this.comboBoxZoom.SelectedIndex + 1;

            if (setStatus == true)
            {
                // then write to status
                this.labelPosition.Text = this.curX + "," + this.curY;
                this.labelTile.Text = TileSystem.TileXYToQuadKey(this.curX, this.curY, this.curZoom);
                double lat; double lon;
                TileSystem.PixelXYToLatLong(this.curX * 256, this.curY * 256, this.curZoom, out lat, out lon);
                this.labelCoords.Text = "N" + String.Format("{0:0.00000}", lat) + "°, E" + String.Format("{0:0.00000}", lon) + "°";
            }
        }

        public bool hasHiRes(byte[] imagdata)
        {
            // recognize image
            if (imagdata[0x3E] == 0x76 && imagdata[0x3F] == 0x66)
            {
                // it's a red tile
                this.labelRec.Text = "RED tile";
                return false;
            }
            else
            {
                this.labelRec.Text = "no red tile";
                return true;
            }
        }

        /// <summary>
        /// Downloads and looks to the surrounding tiles (specifically the one on "this", the front, the left and the previous state)
        /// </summary>
        public void decideNext()
        {
            // update status
            this.numberOfTiles++;
            this.labelCount.Text = this.numberOfTiles.ToString();
            this.labelPosition.Text = this.curX + "," + this.curY;
            this.labelTile.Text = TileSystem.TileXYToQuadKey(this.curX, this.curY, this.curZoom);
            double lat; double lon;
            TileSystem.PixelXYToLatLong(this.curX * 256 -128, this.curY * 256 -128, this.curZoom, out lat, out lon);
            this.labelCoords.Text = "N" + String.Format("{0:0.00000}", lat) + "°, E" + String.Format("{0:0.00000}", lon) + "°";

            // find out if we reached the starting point again
            if (this.leavedEntry==true && this.labelTile.Text.Equals(this.entryTile) /*&& this.curDir.Equals(this.entryDir)*/)
            {
                this.numberOfTilesMAX = 0; // make it stop
                this.leavedEntry = false;
                this.labelRec.Text = "Reached entry Tile. Stop.";
                return;
            }

            // get files and recognize them:
            bool imgThis = true;
            bool imgFront = true;
            bool imgSide = false;

            byte[] ibThis;
            this.pictureBox1.Image = getPic(this.curX, this.curY, out ibThis);
            imgThis = hasHiRes(ibThis);

            byte[] ibFront;
            this.pbFront.Image = getPic(this.curX + this.dirFx, this.curY + this.dirFy, out ibFront);
            imgFront = hasHiRes(ibFront);

            byte[] ibSide;
            this.pbSide.Image = getPic(this.curX + this.dirSx, this.curY + this.dirSy, out ibSide);
            imgSide = hasHiRes(ibSide);


            // get some tiles left and right too
            //getPic(this.curX + this.dirSx * 2, this.curY + this.dirSy * 2, out ibSide);
            //getPic(this.curX + this.dirSx * -2, this.curY + this.dirSy * -2, out ibSide);
            //getPic(this.curX + this.dirSx * -1, this.curY + this.dirSy * -1, out ibSide);

            BackgroundWorker w = new BackgroundWorker();
            w.DoWork += new DoWorkEventHandler(w_DoWork);
            w.RunWorkerAsync(makeURL(this.curX + this.dirSx * 2, this.curY + this.dirSy * 2));

            BackgroundWorker w2 = new BackgroundWorker();
            w2.DoWork += new DoWorkEventHandler(w_DoWork);
            w2.RunWorkerAsync(makeURL(this.curX + this.dirSx * -2, this.curY + this.dirSy * -2));

            BackgroundWorker w3 = new BackgroundWorker();
            w3.DoWork += new DoWorkEventHandler(w_DoWork);
            w3.RunWorkerAsync(makeURL(this.curX + this.dirSx * -1, this.curY + this.dirSy * -1));

            if (this.isOnLine == true && imgThis == true && imgFront == true && imgSide == true)
            {
                // special case, a inner corner where it has to rotate left instead of right.
                // TODO: sometimes it always rotates when in a green field. this.isOnLine wrong resetted?
                rotate('l');
                moveForward();
                this.isOnLine = false;
            }
            else if(imgThis == false)
            {
                // within a red field, just move forward and continue searching
                moveForward();
                this.isOnLine = false;

                if (imgFront == true)
                {
                    // the next tile (where to it has been moved) is green, so rotate left to start searching in this green field. ===== here, start coordinates have to be taken.
                    rotate('l');

                    this.entryTile = TileSystem.TileXYToQuadKey(this.curX,this.curY,this.curZoom);
                    //this.entryDir = this.curDir;
                    this.labelEntry.Text = this.entryTile;
                    this.leavedEntry = false;
                }
            }
            else if(imgThis == true && imgFront == true)
            {
                moveForward();
                if (imgSide == false)
                    this.isOnLine = true; // navigating on edge
                else
                    this.isOnLine = false; // within a green field, progresses until it hits a red border
            }
            else if (imgThis == true && imgFront == false)
            {
                if (imgSide == false)
                    rotate('r');    // hit edge, simply rotate
                else
                {
                    rotate('r');    // hit red border from within a green field. ===== here, start coordinates have to be taken.
                    this.entryTile = this.labelTile.Text;
                    //this.entryDir = this.curDir;
                    this.labelEntry.Text = this.entryTile;
                    this.leavedEntry = false;
                }
            }
        }

        // backgroundworker which fetches more images
        void w_DoWork(object sender, DoWorkEventArgs e)
        {
            byte[] temp;
            getPic(e.Argument.ToString(), out temp);
        }

        public void addHistory(string tile, int zoom)
        {
            // add to history
            int upperzoom = zoom - 2;
            if (this.history.ContainsKey(upperzoom) == true)
            {
                string uppertile = tile.Substring(0, upperzoom);
                if (this.history[upperzoom].Contains(uppertile) == false)
                {
                    this.history[upperzoom].Add(uppertile);
                    this.textBoxHistory.AppendText(uppertile + " ");
                    this.textBoxHistory.ScrollToCaret();

                    this.addHistory(uppertile, upperzoom);
                }
            }
        }

        /// <summary>
        /// moves the "this"-pointer to the next field
        /// </summary>
        public void moveForward()
        {
            string oldtile = TileSystem.TileXYToQuadKey(this.curX, this.curY, this.curZoom);

            this.addHistory(oldtile,this.curZoom);

            this.curX += this.dirFx;
            this.curY += this.dirFy;

            if (this.entryTile != oldtile)
            {
                this.leavedEntry = true;
            }

            // todo: pictureboxes moving
        }

        /// <summary>
        /// rotates the current direction right 'r' or left 'l'.
        /// </summary>
        /// <param name="direction">'r' or 'l'</param>
        public void rotate(char direction)
        {
            // left rotation is 3 right rotations
            if (direction == 'l')
            {
                rotate('r');
                rotate('r');
                rotate('r');
                return;
            }


            int rotX = -1;
            int rotY = 1;

            int newX = this.dirFx != 0 ? 0 : (this.dirFy * rotX);
            int newY = this.dirFy != 0 ? 0 : (this.dirFx * rotY);

            int newSX = this.dirSx != 0 ? 0 : (this.dirSy * rotX);
            int newSY = this.dirSy != 0 ? 0 : (this.dirSx * rotY);

            // hm.. is it really that easy?  trinary operators ftw :-)

            this.dirFx = newX;
            this.dirFy = newY;
            this.dirSx = newSX;
            this.dirSy = newSY;

            switch (this.curDir)
            {
                case 'u': this.curDir = 'r'; break;
                case 'r': this.curDir = 'd'; break;
                case 'd': this.curDir = 'l'; break;
                case 'l': this.curDir = 'u'; break;
                default:
                    break;
            }
            

            switch (this.curDir)
            {
                case 'u': this.labelDirection.Text = "^"; this.pbFront = this.pictureBox2; this.pbSide = this.pictureBox4; break;
                case 'r': this.labelDirection.Text = ">"; this.pbFront = this.pictureBox8; this.pbSide = this.pictureBox2; break;
                case 'd': this.labelDirection.Text = "v"; this.pbFront = this.pictureBox6; this.pbSide = this.pictureBox8; break;
                case 'l': this.labelDirection.Text = "<"; this.pbFront = this.pictureBox4; this.pbSide = this.pictureBox6; break;
                default:
                    break;
            }

            this.pictureBox2.Image = null;
            this.pictureBox4.Image = null;
            this.pictureBox6.Image = null;
            this.pictureBox8.Image = null;
        }

        public string makeURL(int X, int Y)
        {
            string tile = TileSystem.TileXYToQuadKey(X, Y, this.curZoom);
            return makeURL(tile);
        }

        public string makeURL(string tile)
        {
            return "http://ant.dev.openstreetmap.org/bingimageanalyzer/tile.php?t="+tile+"&force=";
        }

        public Image getPic(int X, int Y, out byte[] rawData)
        {
            string url = makeURL(X, Y);
            return getPic(url, out rawData);
        }

        public Image getPic(string url, out byte[] rawData)
        {
            rawData = this.downloadData(url); //DownloadData function from here
            for (int i = 0; i < 10; i++)
            {
                if (rawData != null)
                    break;
                rawData = this.downloadData(url); //DownloadData function from here
            }

            MemoryStream stream = new MemoryStream(rawData);
            Image img = Image.FromStream(stream);
            stream.Close();
            return img;
        }

        /// <summary>
        /// Tries to download a file to the byte-array. Returns null if not possible
        /// </summary>
        private byte[] downloadData(string url)
        {
            try
            {
                WebRequest req = WebRequest.Create(url);
                WebResponse response = req.GetResponse();
                Stream stream = response.GetResponseStream();

                byte[] buffer = new byte[1024*20];
                int numBytesRead = stream.Read(buffer, 0, buffer.Length);

                Application.DoEvents();

                stream.Close();
                response.Close();
                req.Abort();

                return buffer;
            }
            catch (Exception)
            {
                MessageBox.Show("There was a problem downloading the file");
                return null;
            }
        }

        private void buttonAbout_Click(object sender, EventArgs e)
        {
            MessageBox.Show("This application does edge-detection for bing high resolution aerial image tiles. It uses the /bingimageanalyzer/ from ant.\r\nWhat currently does definitely NOT work: detection over the 0-meridian.\r\nIf you are interested in this application, write to osm@quarksteilchen.fastmail.fm", "About",MessageBoxButtons.OK,MessageBoxIcon.Information);
        }


        private void buttonPic_Click(object sender, EventArgs e)
        {
            this.parsePositions(true);

            Bitmap b = new Bitmap("hellokitty.bmp");
            // 12023011320132000  (schönbrunn)

            for(int x = 0; x<b.Width; x++)
            {
                for (int y = 0; y < b.Height; y++)
                {
                    Color c = b.GetPixel(x, y);

                    this.labelRec.Text = c.ToString();

                    // if black pixel .. 
                    if (c.R==0 && c.G==0 && c.B==0)
                    {
                        byte[] temp;
                        getPic(this.curX + x, this.curY + y, out temp);
                        this.numberOfTiles++;
                        this.labelCount.Text = this.numberOfTiles.ToString();
                        this.labelRec.Text += " BLACK TILE";
                    }
                }
            }
            //Color c = b.GetPixel()
        }

        private void buttonClr_Click(object sender, EventArgs e)
        {
            this.textBoxHistory.Clear();

            this.history = new Dictionary<int, List<String>>();
            this.history[14] = new List<string>();
            this.history[12] = new List<string>();
            this.history[10] = new List<string>();
            this.history[8] = new List<string>();
            this.history[6] = new List<string>();
            //this.history[7] = new List<string>();
        }

        private void buttonRunH_Click(object sender, EventArgs e)
        {
            this.clearStatus();
            this.numberOfTiles = 0;

            // count number of all tiles that will be requested:
            int totalNum = 0;
            foreach (int k in this.history.Keys)
            {
                totalNum += this.history[k].Count;
            }

            foreach (int k in this.history.Keys)
            {
                foreach (string tile in this.history[k])
                {
                    this.numberOfTiles++;
                    this.labelCount.Text = this.numberOfTiles.ToString() + "/" + totalNum;
                    this.labelTile.Text = tile;
                    byte[] temp;
                    this.pictureBox1.Image = getPic(makeURL(tile), out temp);
                }
            }
        }

        public void clearStatus()
        {
            this.labelCoords.Text = "";
            this.labelCount.Text = "0";
            this.labelDirection.Text = "";
            this.labelEntry.Text = "";
            this.labelPosition.Text = "";
            this.labelRec.Text = "";
            this.labelTile.Text = "";

            this.pictureBox1.Image = null;
            this.pictureBox2.Image = null;
            this.pictureBox3.Image = null;
            this.pictureBox4.Image = null;
            this.pictureBox5.Image = null;
            this.pictureBox6.Image = null;
            this.pictureBox7.Image = null;
            this.pictureBox8.Image = null;
            this.pictureBox9.Image = null;
        }

        private void buttonCancel_Click(object sender, EventArgs e)
        {
            this.cancelAll = true;
            this.labelRec.Text = "Cancelled (i)";
        }
    }
}
