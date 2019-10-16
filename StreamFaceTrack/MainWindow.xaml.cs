using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Drawing;
using System.Security.Cryptography.X509Certificates;
using System.Threading;

using SharpOSC;

namespace StreamFaceTrack
{
    /// <summary>
    /// Логика взаимодействия для MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private PXCMSession session;
        private PXCMSenseManager senseManager;
        private PXCMFaceModule faceModule;
        private PXCMFaceConfiguration faceConfiguration;
        private PXCMFaceConfiguration.LandmarksConfiguration landMarkConfiguration;
        private PXCMFaceConfiguration.ExpressionsConfiguration expressionConfiguration;
        private Int32 numberTrackedFace;
        public float headRoll;
        public float headYaw;
        public float headPitch;
        private int faceRectangleWidth;
        private int faceRectangleHeight;
        private int faceRectangleX;
        private int faceRectangleY;
        private float faceAverageDepth;
        private Thread captureProcess;
        private const int LandMarkAlingment = -3;
        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        public static extern bool DeleteObject(IntPtr handle);
        public static BitmapSource bitmapSource;
        public static IntPtr intPointer;
        public static BitmapSource BitmapToBitmapSource(System.Drawing.Bitmap bitMap)
        {
            intPointer = bitMap.GetHbitmap();
            bitmapSource = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(intPointer, IntPtr.Zero, System.Windows.Int32Rect.Empty, System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());

            DeleteObject(intPointer);
            return bitmapSource;
        }

        PointF point = new PointF();




        static string CurrentIpAdress;
        static string currentPort;

        public Int32 Connect = 0;

        public UDPSender Sender;

        public struct expressionsResult
        {
            string expressionName;
            int score;

            public expressionsResult(string expressionName, int score)
            {
                this.expressionName = expressionName;
                this.score = score;
            }
        }


        public MainWindow()
        {
            InitializeComponent();
            session = PXCMSession.CreateInstance();
            var version = session.QueryVersion();
            Console.WriteLine("SDK Version {0}.{1}", version.major, version.minor);
        }

        private void startButton_Click(object sender, RoutedEventArgs e)
        {
            CurrentIpAdress = ipTextBox.Text;
            currentPort = portTextBox.Text;

           

            senseManager = PXCMSenseManager.CreateInstance();
            senseManager.EnableStream(PXCMCapture.StreamType.STREAM_TYPE_COLOR, 640, 480, 60);
            senseManager.EnableFace();
            senseManager.Init();
            faceModule = senseManager.QueryFace();
            faceConfiguration = faceModule.CreateActiveConfiguration();
            faceConfiguration.detection.isEnabled = true;
            expressionConfiguration = faceConfiguration.QueryExpressions();
            expressionConfiguration.Enable();
            expressionConfiguration.EnableAllExpressions();
            faceConfiguration.landmarks.isEnabled = true;
            faceConfiguration.landmarks.numLandmarks = 78;
            faceConfiguration.EnableAllAlerts();
            faceConfiguration.ApplyChanges();
            captureProcess = new Thread(new ThreadStart(CaptureProcess));

            

            captureProcess.Start();
        }

        private void stopButton_Click(object sender, RoutedEventArgs e)
        {
            //Sender.Close();
            captureProcess.Abort();
            senseManager.Dispose();
        }

        private void CaptureProcess()
        {
            Sender = new UDPSender(CurrentIpAdress, Convert.ToInt32(currentPort));
            while (senseManager.AcquireFrame(true).IsSuccessful())
            {
                PXCMCapture.Sample sample = senseManager.QuerySample();
                PXCMImage.ImageData colorImageData;
                Bitmap colorBitmap;

                sample.color.AcquireAccess(PXCMImage.Access.ACCESS_READ, PXCMImage.PixelFormat.PIXEL_FORMAT_RGB32, out colorImageData);
                colorBitmap = colorImageData.ToBitmap(0, sample.color.info.width, sample.color.info.height);

                if (faceModule != null)
                {
                    PXCMFaceData faceData = faceModule.CreateOutput();
                    faceData.Update();
                    numberTrackedFace = faceData.QueryNumberOfDetectedFaces();

                    PXCMFaceData.Face faceDataFace = faceData.QueryFaceByIndex(0);

                    if (faceDataFace != null)
                    {
                        PXCMFaceData.DetectionData faceDetectionData = faceDataFace.QueryDetection();
                        PXCMFaceData.LandmarksData landMarksData = faceDataFace.QueryLandmarks();


                        if (faceDetectionData != null) //Запись переменных нахождения фейса
                        {
                            PXCMRectI32 faceRectangle;
                            faceDetectionData.QueryFaceAverageDepth(out faceAverageDepth);
                            faceDetectionData.QueryBoundingRect(out faceRectangle);
                            faceRectangleHeight = faceRectangle.h;
                            faceRectangleWidth = faceRectangle.w;
                            faceRectangleX = faceRectangle.x;
                            faceRectangleY = faceRectangle.y;

                            PXCMFaceData.LandmarkPoint[] points; //Херачим точки на фейс
                            if (landMarksData != null)
                            {
                                bool res = landMarksData.QueryPoints(out points);

                                Graphics graphics = Graphics.FromImage(colorBitmap);
                                Font font = new Font(System.Drawing.FontFamily.GenericMonospace, 12, System.Drawing.FontStyle.Bold);

                                foreach (PXCMFaceData.LandmarkPoint landmark in points)
                                {
                                    point.X = landmark.image.x + LandMarkAlingment;
                                    point.Y = landmark.image.y + LandMarkAlingment;
                                    //Console.WriteLine(point.X);

                                    if (landmark.confidenceImage == 0)
                                    {
                                        graphics.DrawString("X", font, System.Drawing.Brushes.Brown, point);
                                        
                                    }
                                    else
                                    {
                                        graphics.DrawString("*", font, System.Drawing.Brushes.CornflowerBlue, point);
                                    }
                                    Connect = Math.Min(landmark.confidenceImage, 1);
                                }
                            }


                        }

                        var connectMessage = new SharpOSC.OscMessage("/expressions/connectMessage", Connect);
                        Sender.Send(connectMessage);

                        PXCMFaceData.PoseData facePoseData = faceDataFace.QueryPose(); //переменные поворота для анимации головы
                        if (facePoseData != null)
                        {
                            PXCMFaceData.PoseEulerAngles headAngles;
                            facePoseData.QueryPoseAngles(out headAngles);
                            headRoll = headAngles.roll;
                            headYaw = headAngles.yaw;
                            headPitch = headAngles.pitch;
                        }

                        PXCMFaceData.ExpressionsData expressionData = faceDataFace.QueryExpressions();

                        if (expressionData != null)
                        {
                            PXCMFaceData.ExpressionsData.FaceExpressionResult score;

                            expressionData.QueryExpression(PXCMFaceData.ExpressionsData.FaceExpression.EXPRESSION_KISS, out score);
                            Dispatcher.Invoke(() => kissExpression.Text = Convert.ToString(score.intensity));
                            var kissMessage = new SharpOSC.OscMessage("/expressions/kiss", score.intensity);
                            Sender.Send(kissMessage);

                            expressionData.QueryExpression(PXCMFaceData.ExpressionsData.FaceExpression.EXPRESSION_MOUTH_OPEN, out score);
                            Dispatcher.Invoke(() => mouthExpression.Text = Convert.ToString(score.intensity));
                            var mouthMessage = new SharpOSC.OscMessage("/expressions/mouth", score.intensity);
                            Sender.Send(mouthMessage);


                            expressionData.QueryExpression(PXCMFaceData.ExpressionsData.FaceExpression.EXPRESSION_SMILE, out score);
                            Dispatcher.Invoke(() => smileExpression.Text = Convert.ToString(score.intensity));
                            var smileMessage = new SharpOSC.OscMessage("/expressions/smile", score.intensity);
                            Sender.Send(smileMessage);

                            expressionData.QueryExpression(PXCMFaceData.ExpressionsData.FaceExpression.EXPRESSION_TONGUE_OUT, out score);
                            Dispatcher.Invoke(() => tongueExpression.Text = Convert.ToString(score.intensity));
                            var tongueOutMessage = new SharpOSC.OscMessage("/expressions/tongueout", score.intensity);
                            Sender.Send(tongueOutMessage);


                            expressionData.QueryExpression(PXCMFaceData.ExpressionsData.FaceExpression.EXPRESSION_BROW_LOWERER_LEFT, out score);
                            Dispatcher.Invoke(() => leftBrowLowExpression.Text = Convert.ToString(score.intensity));
                            var leftBrowLowMessage = new SharpOSC.OscMessage("/expressions/leftBrowLow", score.intensity);
                            Sender.Send(leftBrowLowMessage);


                            expressionData.QueryExpression(PXCMFaceData.ExpressionsData.FaceExpression.EXPRESSION_BROW_LOWERER_RIGHT, out score);
                            Dispatcher.Invoke(() => rightBrowLowExpression.Text = Convert.ToString(score.intensity));
                            var rightBrowLowMessage = new SharpOSC.OscMessage("/expressions/rightBrowLow", score.intensity);
                            Sender.Send(rightBrowLowMessage);


                            expressionData.QueryExpression(PXCMFaceData.ExpressionsData.FaceExpression.EXPRESSION_BROW_RAISER_LEFT, out score);
                            Dispatcher.Invoke(() => leftBrowRaiseExpression.Text = Convert.ToString(score.intensity));
                            var leftBrowRaiseMessage = new SharpOSC.OscMessage("/expressions/leftBrowRaise", score.intensity);
                            Sender.Send(leftBrowRaiseMessage);

                            expressionData.QueryExpression(PXCMFaceData.ExpressionsData.FaceExpression.EXPRESSION_BROW_RAISER_RIGHT, out score);
                            Dispatcher.Invoke(() => rightBrowRaiseExpression.Text = Convert.ToString(score.intensity));
                            var rightBrowRaiseMessage = new SharpOSC.OscMessage("/expressions/rightBrowRaise", score.intensity);
                            Sender.Send(rightBrowRaiseMessage);


                            expressionData.QueryExpression(PXCMFaceData.ExpressionsData.FaceExpression.EXPRESSION_EYES_CLOSED_LEFT, out score);
                            Dispatcher.Invoke(() => leftEyeClosedExpression.Text = Convert.ToString(score.intensity));
                            var leftEyeClosedMessage = new SharpOSC.OscMessage("/expressions/leftEyeClosed", score.intensity);
                            Sender.Send(leftEyeClosedMessage);


                            expressionData.QueryExpression(PXCMFaceData.ExpressionsData.FaceExpression.EXPRESSION_EYES_CLOSED_RIGHT, out score);
                            Dispatcher.Invoke(() => rightEyeClosedExpression.Text = Convert.ToString(score.intensity));
                            var rightEyeClosedMessage = new SharpOSC.OscMessage("/expressions/rightEyeClosed", score.intensity);
                            Sender.Send(rightEyeClosedMessage);


                            expressionData.QueryExpression(PXCMFaceData.ExpressionsData.FaceExpression.EXPRESSION_EYES_TURN_LEFT, out score);
                            Dispatcher.Invoke(() => eyesTurnLeftExpression.Text = Convert.ToString(score.intensity));
                            var eyesTurnLeftMessage = new SharpOSC.OscMessage("/expressions/eyesTurnLeft", score.intensity);
                            Sender.Send(eyesTurnLeftMessage);


                            expressionData.QueryExpression(PXCMFaceData.ExpressionsData.FaceExpression.EXPRESSION_EYES_TURN_RIGHT, out score);
                            Dispatcher.Invoke(() => eyesTurnRightExpression.Text = Convert.ToString(score.intensity));
                            var eyesTurnRightMessage = new SharpOSC.OscMessage("/expressions/eyesTurnRight", score.intensity);
                            Sender.Send(eyesTurnRightMessage);



                            expressionData.QueryExpression(PXCMFaceData.ExpressionsData.FaceExpression.EXPRESSION_EYES_DOWN, out score);
                            Dispatcher.Invoke(() => eyesDownExpression.Text = Convert.ToString(score.intensity));
                            var eyesDownMessage = new SharpOSC.OscMessage("/expressions/eyesDown", score.intensity);
                            Sender.Send(eyesDownMessage);


                            expressionData.QueryExpression(PXCMFaceData.ExpressionsData.FaceExpression.EXPRESSION_EYES_UP, out score);
                            Dispatcher.Invoke(() => eyesUpExpression.Text = Convert.ToString(score.intensity));
                            var eyesUpMessage = new SharpOSC.OscMessage("/expressions/eyesUp", score.intensity);
                            Sender.Send(eyesUpMessage);


                            expressionData.QueryExpression(PXCMFaceData.ExpressionsData.FaceExpression.EXPRESSION_PUFF_LEFT, out score);
                            Dispatcher.Invoke(() => puffLeftExpression.Text = Convert.ToString(score.intensity));
                            var leftPuffMessage = new SharpOSC.OscMessage("/expressions/leftPuff", score.intensity);
                            Sender.Send(leftPuffMessage);



                            expressionData.QueryExpression(PXCMFaceData.ExpressionsData.FaceExpression.EXPRESSION_PUFF_RIGHT, out score);
                            Dispatcher.Invoke(() => puffRightExpression.Text = Convert.ToString(score.intensity));
                            var rightPuffMessage = new SharpOSC.OscMessage("/expressions/rightPuff", score.intensity);
                            Sender.Send(rightPuffMessage);



                        }
                    }
                    faceData.Dispose();
                }
                UpdateUI(colorBitmap);

                colorBitmap.Dispose();
                sample.color.ReleaseAccess(colorImageData);
                senseManager.ReleaseFrame();

            }
        }

        public void Calibration()
        {

        }


        private void UpdateUI(Bitmap bitmap) //Выводим рожу
        {
            this.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Normal, new Action(delegate ()
            {
                if (bitmap != null)
                {
                    imageBase.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
                    ScaleTransform mainTransform = new ScaleTransform();
                    mainTransform.ScaleX = -1;
                    mainTransform.ScaleY = 1;
                    imageBase.RenderTransform = mainTransform;


                    headRollResult.Text = headRoll.ToString();
                    var headRollMessage = new SharpOSC.OscMessage("/rotation/roll", headRoll);
                    Sender.Send(headRollMessage);
                    headYawResult.Text = headYaw.ToString();
                    var headYawMessage = new SharpOSC.OscMessage("/rotation/yaw", headYaw);
                    Sender.Send(headYawMessage);
                    headPitchResult.Text = headPitch.ToString();
                    var headPitchMessage = new SharpOSC.OscMessage("/rotation/pitch", headPitch);
                    Sender.Send(headPitchMessage);


                    Graphics graphics = Graphics.FromImage(bitmap); //Херачим прямоугольник

                    System.Drawing.Pen pen = new System.Drawing.Pen(System.Drawing.Color.DeepSkyBlue);
                    graphics.DrawRectangle(pen, faceRectangleX, faceRectangleY, faceRectangleWidth, faceRectangleHeight);

                    imageBase.Source = BitmapToBitmapSource(bitmap);

                }
            }));
        }

    }
}
