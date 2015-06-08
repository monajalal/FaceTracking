//--------------------------------------------------------------------------------------
// Copyright 2015 Intel Corporation
// All Rights Reserved
//
// Permission is granted to use, copy, distribute and prepare derivative works of this
// software for any purpose and without fee, provided, that the above copyright notice
// and this statement appear in all copies.  Intel makes no representations about the
// suitability of this software for any purpose.  THIS SOFTWARE IS PROVIDED "AS IS."
// INTEL SPECIFICALLY DISCLAIMS ALL WARRANTIES, EXPRESS OR IMPLIED, AND ALL LIABILITY,
// INCLUDING CONSEQUENTIAL AND OTHER INDIRECT DAMAGES, FOR THE USE OF THIS SOFTWARE,
// INCLUDING LIABILITY FOR INFRINGEMENT OF ANY PROPRIETARY RIGHTS, AND INCLUDING THE
// WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE.  Intel does not
// assume any responsibility for any errors which may appear in this software nor any
// responsibility to update it.
//--------------------------------------------------------------------------------------
using System;
using System.Text;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Threading;
using System.Drawing;
using System.Windows.Controls;
using System.IO;
using FaceExpression = PXCMFaceData.ExpressionsData.FaceExpression;

namespace FaceID
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private Thread processingThread;
        private PXCMSenseManager senseManager;
        private PXCMFaceConfiguration.RecognitionConfiguration recognitionConfig;
        private PXCMFaceConfiguration faceConfig;
        private PXCMFaceConfiguration.ExpressionsConfiguration expressionConfig;
        private PXCMFaceData faceData;
        private PXCMFaceData.RecognitionData recognitionData;
        private Int32 numFacesDetected;
        private string userId;
        private string dbState;
        private const int DatabaseUsers = 10;
        private const string DatabaseName = "UserDB";
        private const string DatabaseFilename = "database.bin";
        private bool doRegister;
        private bool doUnregister;
        private int faceRectangleHeight;
        private int faceRectangleWidth;
        private int faceRectangleX;
        private int faceRectangleY;
        private int eyesUp; //eyes up score
        private Boolean eyeIsUP; //checking to see if eye is up with a threshold
        private double yaw;
        private double pitch;
        private double roll;
        //private string fileName = System.IO.Path.GetRandomFileName();
        private string fileName = "results.txt";
        private string filePath = Directory.GetCurrentDirectory();
        string pathString;
        



        public MainWindow()
        {
            InitializeComponent();
            rectFaceMarker.Visibility = Visibility.Hidden;
            chkShowFaceMarker.IsChecked = true;
            numFacesDetected = 0;
            userId = string.Empty;
            dbState = string.Empty;
            doRegister = false;
            doUnregister = false;
            pathString  = System.IO.Path.Combine(filePath, fileName);
            System.IO.File.WriteAllText(pathString, string.Empty);
            File.WriteAllText(pathString, "Timestamp,roll,pitch, yaw, eyes_up\r\n" + File.ReadAllText(pathString));
            /*
            string[] lines = System.IO.File.ReadAllLines(pathString);
            foreach (string line in lines)
            {
                if (line==lines[0])
                    System.IO.File.WriteAllText(pathString,"Timestamp,roll,pitch, yaw, eyes_up");
            }
            */




            // Start SenseManage and configure the face module
            ConfigureRealSense();

            // Start the worker thread
            processingThread = new Thread(new ThreadStart(ProcessingThread));
            processingThread.Start();
        }

        private void ConfigureRealSense()
        {
            PXCMFaceModule faceModule;
            

            
            // Start the SenseManager and session  
            senseManager = PXCMSenseManager.CreateInstance();

            // Enable the color stream
            senseManager.EnableStream(PXCMCapture.StreamType.STREAM_TYPE_COLOR, 640, 480, 60);

            // Enable the face module
            senseManager.EnableFace();
            faceModule = senseManager.QueryFace();
            faceConfig = faceModule.CreateActiveConfiguration();
            faceConfig.detection.isEnabled = true;
            expressionConfig = faceConfig.QueryExpressions();
            expressionConfig.Enable();
            expressionConfig.EnableAllExpressions();
            faceConfig.EnableAllAlerts();






            // Configure for 3D face tracking (if camera cannot support depth it will revert to 2D tracking)
            faceConfig.SetTrackingMode(PXCMFaceConfiguration.TrackingModeType.FACE_MODE_COLOR_PLUS_DEPTH);

            // Enable facial recognition
            recognitionConfig = faceConfig.QueryRecognition();
            recognitionConfig.Enable();
            

            // Create a recognition database
            PXCMFaceConfiguration.RecognitionConfiguration.RecognitionStorageDesc recognitionDesc = new PXCMFaceConfiguration.RecognitionConfiguration.RecognitionStorageDesc();
            recognitionDesc.maxUsers = DatabaseUsers;
            recognitionConfig.CreateStorage(DatabaseName, out recognitionDesc);
            recognitionConfig.UseStorage(DatabaseName);
            LoadDatabaseFromFile();
            recognitionConfig.SetRegistrationMode(PXCMFaceConfiguration.RecognitionConfiguration.RecognitionRegistrationMode.REGISTRATION_MODE_CONTINUOUS);

            // Apply changes and initialize
            faceConfig.ApplyChanges();
            senseManager.Init();
            faceData = faceModule.CreateOutput();
            int numFaces = faceData.QueryNumberOfDetectedFaces();
            Console.WriteLine("number of detected faces", numFaces);

            // Mirror image
            senseManager.QueryCaptureManager().QueryDevice().SetMirrorMode(PXCMCapture.Device.MirrorMode.MIRROR_MODE_HORIZONTAL);

            // Release resources
            faceConfig.Dispose();
            faceModule.Dispose();
         }


        private int GetFaceExpressionIntensity(PXCMFaceData.ExpressionsData data, FaceExpression faceExpression)
        {
            PXCMFaceData.ExpressionsData.FaceExpressionResult score;
            data.QueryExpression(faceExpression, out score);
            //if (score.intensity > 0) Debug.WriteLine(faceExpression + ":" +score.intensity);
            return score.intensity;
        }


        private bool CheckFaceExpression(PXCMFaceData.ExpressionsData data, FaceExpression faceExpression, int threshold)
        {
            return GetFaceExpressionIntensity(data, faceExpression) > threshold;
        }




        private void ProcessingThread()
        {
            // Start AcquireFrame/ReleaseFrame loop
            while (senseManager.AcquireFrame(true) >= pxcmStatus.PXCM_STATUS_NO_ERROR)
            {
                // Acquire the color image data
                PXCMCapture.Sample sample = senseManager.QuerySample();
                Bitmap colorBitmap;
                PXCMImage.ImageData colorData;
                sample.color.AcquireAccess(PXCMImage.Access.ACCESS_READ, PXCMImage.PixelFormat.PIXEL_FORMAT_RGB24, out colorData);
                colorBitmap = colorData.ToBitmap(0, sample.color.info.width, sample.color.info.height);
                
                // Get face data
                if (faceData != null)
                {
                    faceData.Update();
                    numFacesDetected = faceData.QueryNumberOfDetectedFaces();

                    if (numFacesDetected > 0)
                    {
                        
                        // Get the first face detected (index 0)
                        PXCMFaceData.Face face = faceData.QueryFaceByIndex(0);
                        face.QueryExpressions();
                        PXCMFaceData.PoseData poseData = face.QueryPose();
                        PXCMPoint3DF32 outHeadPosition = new PXCMPoint3DF32();
                       // poseData.QueryHeadPosition(out outHeadPosition);
                       PXCMFaceData.PoseEulerAngles outPoseEulerAngles = new PXCMFaceData.PoseEulerAngles();
				       poseData.QueryPoseAngles(out outPoseEulerAngles);
                       roll = outPoseEulerAngles.roll;
                       pitch = outPoseEulerAngles.pitch;
                       yaw = outPoseEulerAngles.yaw;
				       Console.WriteLine("Rotation: " + outPoseEulerAngles.roll + " " + outPoseEulerAngles.pitch + " " + outPoseEulerAngles.yaw);
                       PXCMFaceData.ExpressionsData edata = face.QueryExpressions();
                       // retrieve the expression information
                       PXCMFaceData.ExpressionsData.FaceExpressionResult eyesUpScore;
                       edata.QueryExpression(PXCMFaceData.ExpressionsData.FaceExpression.EXPRESSION_EYES_UP, out eyesUpScore);
                       eyesUp = eyesUpScore.intensity;
                       PXCMCapture.Device device = senseManager.captureManager.device;
                       device.SetIVCAMAccuracy(PXCMCapture.Device.IVCAMAccuracy.IVCAM_ACCURACY_FINEST);
                       eyeIsUP= CheckFaceExpression(edata, FaceExpression.EXPRESSION_EYES_UP, 15);
                       
                       var csv = new StringBuilder();
                         // outputs 10:00 PM
                       var newLine = string.Format("{0},{1},{2},{3},{4}{5}", DateTime.Now.ToString("dd-MM-yyyy-hh:mm:ss:fff"), roll, pitch, yaw, eyesUp, Environment.NewLine);
                       csv.Append(newLine);
                      // string pathString = System.IO.Path.Combine(filePath, fileName);

                       File.AppendAllText(pathString, csv.ToString());

                      


                        // Retrieve face location data
                        PXCMFaceData.DetectionData faceDetectionData = face.QueryDetection();
                        if (faceDetectionData != null)
                        {
                            PXCMRectI32 faceRectangle;
                            faceDetectionData.QueryBoundingRect(out faceRectangle);
                            faceRectangleHeight = faceRectangle.h;
                            faceRectangleWidth = faceRectangle.w;
                            faceRectangleX = faceRectangle.x;
                            faceRectangleY = faceRectangle.y;
                        }


                        // Process face recognition data
                        if (face != null)
                        {
                            // Retrieve the recognition data instance
                            recognitionData = face.QueryRecognition();
                            
                            // Set the user ID and process register/unregister logic
                            if (recognitionData.IsRegistered())
                            {
                                userId = Convert.ToString(recognitionData.QueryUserID());   
                            
                                if (doUnregister)
                                {
                                    recognitionData.UnregisterUser();
                                    doUnregister = false;
                                }
                            }
                            else
                            {
                                if (doRegister)
                                {
                                    recognitionData.RegisterUser();

                                    // Capture a jpg image of registered user
                                    colorBitmap.Save("image.jpg", System.Drawing.Imaging.ImageFormat.Jpeg);

                                    doRegister = false;
                                }
                                else
                                {
                                    userId = "Unrecognized";
                                }
                            }
                        }
                    }
                    else
                    {
                        userId = "No users in view";
                    }
                }

                // Display the color stream and other UI elements
                UpdateUI(colorBitmap);

                // Release resources
                colorBitmap.Dispose();
                sample.color.ReleaseAccess(colorData);
                sample.color.Dispose();

                // Release the frame
                senseManager.ReleaseFrame();
            }
        }

        private void UpdateUI(Bitmap bitmap)
        {
            this.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Normal, new Action(delegate()
            {
                // Display  the color image
                if (bitmap != null)
                {
                    imgColorStream.Source = ConvertBitmap.BitmapToBitmapSource(bitmap);
                }

                 // Update UI elements
                lblNumFacesDetected.Content = String.Format("Faces Detected: {0}", numFacesDetected);
                lblUserId.Content = String.Format("User ID: {0}", userId);
                lblDatabaseState.Content = String.Format("Database: {0}", dbState);
                lblExpression.Content = string.Format("Eyes Up: {0}", eyesUp);
                lblExpressionThreshold.Content = string.Format("Eyes UP W threshold:{0}", eyeIsUP);
                lblYaw.Content = string.Format("Yaw: {0}", yaw);
                lblPitch.Content = string.Format("Pitch: {0}", pitch);
                lblRoll.Content = string.Format("Roll: {0}", roll);

                // Change picture border color depending on if user is in camera view
                if (numFacesDetected > 0)
                {
                    bdrPictureBorder.BorderBrush = System.Windows.Media.Brushes.LightGreen;
                }
                else
                {
                    bdrPictureBorder.BorderBrush = System.Windows.Media.Brushes.Red;
                }

                // Show or hide face marker
                if ((numFacesDetected > 0) && (chkShowFaceMarker.IsChecked == true))
                {
                    // Show face marker
                    rectFaceMarker.Height = faceRectangleHeight;
                    rectFaceMarker.Width = faceRectangleWidth;
                    Canvas.SetLeft(rectFaceMarker, faceRectangleX);
                    Canvas.SetTop(rectFaceMarker, faceRectangleY);
                    rectFaceMarker.Visibility = Visibility.Visible;

                    // Show floating ID label
                    lblFloatingId.Content = String.Format("User ID: {0}", userId);
                    Canvas.SetLeft(lblFloatingId, faceRectangleX);
                    Canvas.SetTop(lblFloatingId, faceRectangleY - 20);
                    lblFloatingId.Visibility = Visibility.Visible;
                }
                else
                {
                    // Hide the face marker and floating ID label
                    rectFaceMarker.Visibility = Visibility.Hidden;
                    lblFloatingId.Visibility = Visibility.Hidden;
                }
            }));

            // Release resources
            bitmap.Dispose();
        }

        private void LoadDatabaseFromFile()
        {
            if (File.Exists(DatabaseFilename))
            {
                Byte[] buffer = File.ReadAllBytes(DatabaseFilename);
                recognitionConfig.SetDatabaseBuffer(buffer);
                dbState = "Loaded";
            }
            else
            {
                dbState = "Not Found";
            }
        }

        private void SaveDatabaseToFile()
        {
            // Allocate the buffer to save the database
            PXCMFaceData.RecognitionModuleData recognitionModuleData = faceData.QueryRecognitionModule();
            Int32 nBytes = recognitionModuleData.QueryDatabaseSize();
            Byte[] buffer = new Byte[nBytes];

            // Retrieve the database buffer
            recognitionModuleData.QueryDatabaseBuffer(buffer);

            // Save the buffer to a file
            // (NOTE: production software should use file encryption for privacy protection)
            File.WriteAllBytes(DatabaseFilename, buffer);
            dbState = "Saved";
        }

        private void DeleteDatabaseFile()
        {
            if (File.Exists(DatabaseFilename))
            {
                File.Delete(DatabaseFilename);
                dbState = "Deleted";
            }
            else
            {
                dbState = "Not Found";
            }
        }

        private void ReleaseResources()
        {
            // Stop the worker thread
            processingThread.Abort();

            // Release resources
            faceData.Dispose();
            senseManager.Dispose();
        }

        private void btnRegister_Click(object sender, RoutedEventArgs e)
        {
            doRegister = true;
        }

        private void btnUnregister_Click(object sender, RoutedEventArgs e)
        {
            doUnregister = true;
        }

        private void btnSaveDatabase_Click(object sender, RoutedEventArgs e)
        {
            SaveDatabaseToFile();
        }

        private void btnDeleteDatabase_Click(object sender, RoutedEventArgs e)
        {
            DeleteDatabaseFile();
        }
        private void btnExit_Click(object sender, RoutedEventArgs e)
        {
            ReleaseResources();
            this.Close();
        }
        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            ReleaseResources();
        }
    }
}
