﻿/*
 * Created in SharpDevelop.
 * User: Dunbaratu
 * Date: 7/3/2014
 * Time: 1:14 AM
 *
 * This file is part of LaserDist - a freely available module
 * for Kerbal Space Program.
 * Copyright (C) 2014 Steven Mading (aka user "Dunbaratu" on GitHub.)
 * author contact: madings@gmail.com
 *
 * This file, and the other files in this project, are distributed
 * under the terms of the GPL3 (Gnu Public License version 3).
 *
 * You should have received a copy of the GNU General Public License
 * along with this program.  If not, see <http://www.gnu.org/licenses/>.
 */
using System;
using System.Collections.Generic;
using UnityEngine;

namespace LaserDist
{
    /// <summary>
    /// The class associated with the plugin to run the LaserDist
    /// part(s).
    /// </summary>
    public class LaserDistModule : PartModule
    {
        private bool debugMsg = false;
        
        /// <summary>
        ///   Laser's origin relative to the part's coord transform:
        /// </summary>
        static private Vector3d relLaserOrigin;
        /// <summary>
        ///   Laser's origin in Unity World coords:
        /// </summary>
        Vector3d origin;
        /// <summary>
        ///   Laser's pointing unit vector in Unity World coords:
        /// </summary>
        Vector3d pointing;

        /// <summary>
        ///   Laser's origin in the map view's coords:
        /// </summary>
        Vector3d mapOrigin;
        /// <summary>
        ///   Laser's pointing unit vector in the map view's coords:
        /// </summary>
        Vector3d mapPointing;
        
        /// <summary>
        /// The utility that solves raycasts for this laser.
        /// </summary>
        private LaserPQSUtil pqsTool;

        private bool doStressTest = false; // There's a debug test I left in the code that this enables.

        private bool isDrawing = false;
        private bool isOnMap = false;
        private bool isInEditor = false;
        private bool hasPower = false;
        private double prevTime = 0.0;
        private double deltaTime = 0.0;
                
        private GameObject lineObj = null;
        private LineRenderer line = null;
        private Int32 mask;
        private int maskBitDefault = 0;
        private int maskTransparentFX = 1;
        private int maskBitWater = 3;
        private int maskBitPartsList = 8;
        private int maskBitScaledScenery = 10;
        private int maskBitLocalScenery = 15;
        private int maskBitKerbals = 16;
        private int maskBitEditorUI = 17;
        private int maskBitDisconnectedParts = 19;
        private int maskBitPartTriggers= 21;
        private int maskBitWheelColliders = 27;
        private int maskBitTerrainColliders = 28;

        /// <summary>Distance the laser is showing to the first collision:</summary>
        [KSPField(isPersistant=true, guiName = "Distance", guiActive = true, guiActiveEditor = true, guiUnits = "m", guiFormat = "N2")]
        public float Distance = 0.0f;

        /// <summary>Name of thing the laser is hitting:</summary>
        [KSPField(isPersistant=true, guiName = "Hit", guiActive = true, guiActiveEditor = true)]
        public string HitName = "<none>";

        /// <summary>Distance the laser is showing to the first collision:</summary>
        [KSPField(isPersistant=true, guiName = "Max Sensor Range", guiActive = true, guiActiveEditor = true, guiUnits = "m")]
        public float MaxDistance = 10000f;        

        /// <summary>Flag controlling whether or not to see the laserbeam onscreen</summary>
        [KSPField(isPersistant=true, guiName = "Visible", guiActive = true, guiActiveEditor = true),
         UI_Toggle(disabledText="no", enabledText="yes")]
        public bool DrawLaser = false;

        /// <summary>Flag controlling whether or not the device is taking readings</summary>
        [KSPField(isPersistant=true, guiName = "Enabled", guiActive = true, guiActiveEditor = true),
         UI_Toggle(disabledText="no", enabledText="yes")]
        public bool Activated = false;

        /// <summary>electric usage per second that it's on:</summary>
        [KSPField(isPersistant=true, guiName = "Electricity Drain", guiActive = true, guiActiveEditor = true, guiUnits = "/sec", guiFormat = "N2")]
        public float ElectricPerSecond = 0.0f;

        /// <summary>
        /// How greedy is this mod at using the CPU to come up with an answer every tick?  If the value is too large,
        /// then the mod will bog down KSP's animation rate.  If the value is too small, then the mod will take a longer
        /// time to retun an answer and it might take more than one Unity Update to get the answer.
        /// </summary>
        [KSPField(isPersistant=true, guiName = "CPU hog", guiActive = true, guiActiveEditor = true, guiUnits = "%"),
         UI_FloatRange(minValue=1f, maxValue=20f, stepIncrement=1f)]
        public float CPUGreedyPercent;
        
        /// <summary>How long ago was the value you see calculated, in integer number of updates?</summary>
        /// TODO: If support for it ends up in kOS, then explicitly make this KSPField open to kOS without being seen on the menu.
        [KSPField(isPersistant=true, guiName = "Update Age", guiActive = true, guiActiveEditor = true, guiUnits = " update(s)")]
        public int UpdateAge = 0;
        
        /// <summary>
        /// Unity calls this hook during the KSP initial startup screen:
        /// </summary>
        /// <param name="state"></param>
        public override void OnAwake()
        {
            moduleName = "LaserDistModule";
            relLaserOrigin = new Vector3d(0.0,-0.3,0.0);
            pqsTool = new LaserPQSUtil(part);
            pqsTool.tickPortionAllowed = (double) (CPUGreedyPercent / 100.0);
            
            mask =  (1 << maskBitDefault)
                    + (1 << maskBitWater)
                    + (1 << maskBitPartsList)
                    // + (1 << maskBitScaledScenery) // seems to be the map scenery and it finds hits when not on mapview.
                    + (1 << maskBitLocalScenery)
                    + (1 << maskBitKerbals)
                    + (1 << maskBitDisconnectedParts)
                    + (1 << maskBitPartTriggers)
                    + (1 << maskBitWheelColliders)
                    + (1 << maskBitTerrainColliders) ;
            
        }

        public override void OnActive()
        {
            GameEvents.onPartDestroyed.Add( OnLaserDestroy );
        }

        // Actions to control the active flag:
        // ----------------------------------------
        [KSPAction("toggle")]
        public void ActionToggle(KSPActionParam p)
        {
            Activated = ! Activated;
        }

        // Actions to control the visibility flag:
        // ----------------------------------------
        [KSPAction("toggle visibility")]
        public void ActionToggleVisibility(KSPActionParam p)
        {
            DrawLaser = ! DrawLaser;
        }
        
        
        private void ChangeIsDrawing()
        {
            bool newVal = (hasPower && Activated && DrawLaser);
            if( newVal != isDrawing )
            {
                if( newVal )
                {
                    startDrawing();
                }
                else
                {
                    stopDrawing();
                }
            }
            
            isDrawing = newVal;
        }
        
        /// <summary>
        ///   Begin the Unity drawing of this laser,
        ///   making the unity objects for it.
        /// </summary>
        private void startDrawing()
        {
            lineObj = new GameObject("LaserDist beam");
            isOnMap = MapView.MapIsEnabled;
            lineObj.layer = maskTransparentFX;

            line = lineObj.AddComponent<LineRenderer>();
            
            line.material = new Material(Shader.Find("Particles/Additive") );
            Color c1 = new Color( 1.0f,0.0f,0.0f,0.5f);
            Color c2 = new Color( 1.0f,0.0f,0.0f,0.2f);
            line.SetColors( c1, c2 );
            line.enabled = true;
        }
        
        /// <summary>
        ///   Stop the Unity drawing of this laser:
        ///   destroying the unity objects for it.
        /// </summary>
        private void stopDrawing()
        {
            if( line != null )
            {
                line.enabled = false;
                line = null;
            }
            if( lineObj != null )
            {
                lineObj = null;
            } 
        }

        /// <summary>
        /// Make sure to stop the beam picture as the part is blown up:
        /// </summary>
        public void OnLaserDestroy(Part p)
        {
            // To resolve github issue #5:
            // It turns out KSP will call this on ALL part destructions anywhere
            // in the game, not just when the part being destroyed is this one,
            // so don't do anything if it's the wrong part.
            //
            if (p != this.part) return;
            
            Activated = false;
            DrawLaser = false;
            ChangeIsDrawing();                
        }

        /// <summary>
        ///   Gets new distance reading if the device is on,
        ///   and handles the toggling of the display of the laser.
        /// </summary>
        public override void OnUpdate()
        {
            double nowTime = Planetarium.GetUniversalTime();
            
            pqsTool.tickPortionAllowed = (double) (CPUGreedyPercent / 100.0); // just in case user changed it in the slider.

            deltaTime = nowTime - prevTime;
            prevTime = nowTime;

            ChangeIsDrawing();
            drainPower();
            castUpdate();
            drawUpdate();
            
            if( doStressTest )
            {
                // This code was how I judged how many iterations I should allow the PQS
                // algorithm to take per update - it measures how sluggish animation
                // gets if I use the PQS altitude solver a lot per update.  It should never
                // be enabled again unless you're trying to repeat that sort of test.  It
                // bogs down KSP.
                int numQueries = 1000;
                System.Diagnostics.Stopwatch timer = new System.Diagnostics.Stopwatch();
                timer.Start();
                pqsTool.StressTestPQS(part.vessel.GetOrbit().referenceBody, numQueries);
                timer.Stop();
                if( debugMsg ) UnityEngine.Debug.Log( "StressTestPQS: for " + numQueries + ", " + timer.Elapsed.TotalMilliseconds + "millis" );
            }
        }

        public void Update()
        {
            // Normally a PartModule's OnUpdate isn't called when in the
            // editor mode:  This enables it, so the beam will appear
            // when in the VAB:
            isInEditor = HighLogic.LoadedSceneIsEditor;
            if( isInEditor )
                OnUpdate();
        }

        /// <summary>
        /// Use electriccharge, and check if power is out, and if so, disable:
        /// </summary>
        private void drainPower()
        {
            if( Activated )
            {
                float drainThisUpdate = (float) (ElectricPerSecond * deltaTime);
                float actuallyUsed = part.RequestResource("ElectricCharge", drainThisUpdate);
                if( actuallyUsed < drainThisUpdate/2.0 )
                {
                    hasPower = false;
                }
                else
                {
                    hasPower = true;
                }
            }
        }

        /// <summary>
        ///   Recalculates the distance to a hit item, or -1f if nothing
        ///   was hit by the laser.
        /// </summary>
        /// <returns></returns>
        private void castUpdate()
        {
            float newDist = -1f;
            HitName = "<none>";
            if( hasPower & Activated )
            {
                origin = this.part.transform.TransformPoint( relLaserOrigin );
                pointing = this.part.transform.rotation * Vector3d.down;

                // the points on the map-space corresponding to these points is different:
                mapOrigin = ScaledSpace.LocalToScaledSpace( origin );
                mapPointing = pointing;

                RaycastHit hit;
                if( Physics.Raycast(origin, pointing, out hit, MaxDistance, mask) )
                {
                    newDist = hit.distance;
                    
                    // Walk up the UnityGameObject tree trying to find an object that is
                    // something the user will be familiar with:
                    GameObject hitObject = hit.transform.gameObject;
                    if( hitObject != null )
                    {
                        HitName = hitObject.name; // default if the checks below don't work.

                        // Despite the name and what the Unity documentation says,
                        // GetComponentInParent actually looks all the way up the
                        // ancestor list, not just in Parents, so these following
                        // checks are walking up the ancestors to find the one that
                        // has a KSP component assigned to it:
                        if( hitObject.layer == 15 )
                        {
                            CelestialBody body = hitObject.GetComponentInParent<CelestialBody>();
                            if( body != null )
                            {
                                HitName = body.name;
                            }
                        }
                        else
                        {
                            Part part = hitObject.GetComponentInParent<Part>();
                            if( part != null )
                            {
                                HitName = part.name;
                            }
                        }
                        UpdateAge = 0;
                    }
                }
                // If the hit is not found, or it is found but is far enough
                // away that it might be on the other side of the planet, seen
                // through the ocean (which has no collider so raycasts pass
                // through it), then try the more expensive pqs ray cast solver.
                if( newDist < 0 || newDist > 100000 )
                {
                    double pqsDist;
                    CelestialBody pqsBody;
                    bool success = pqsTool.RayCast( origin, pointing, out pqsBody, out pqsDist );
                    if( pqsTool.UpdateAge == 0 )
                    {
                        if( success )
                        {
                            // If it's a closer hit than we have already, then use it:
                            if( pqsDist < newDist || newDist < 0 )
                            {
                                HitName = pqsBody.name;
                                newDist = (float) pqsDist;
                            }
                        }
                    }
                    else
                    {
                        if( pqsTool.PrevSuccess )
                        {
                            // If it's a closer hit than we have already, then use it:
                            if( pqsTool.PrevDist < newDist || newDist < 0 )
                            {
                                HitName = pqsTool.PrevBodyName;
                                newDist = (float) pqsTool.PrevDist;
                            }
                        }
                    }
                    UpdateAge = pqsTool.UpdateAge;
                }
            }
            Distance = newDist;
            
        }

        /// <summary>
        ///   Draws the laser line to visually show the effect.
        ///   (Also useful for debugging).
        /// </summary>
        private void drawUpdate()
        {
            isOnMap = MapView.MapIsEnabled;
            isInEditor = HighLogic.LoadedSceneIsEditor;
            if( isDrawing )
            {
                Vector3d useOrigin = origin;
                Vector3d usePointing = pointing;
                if( isInEditor )
                {
                    lineObj.layer = maskBitDefault;
                }
                else if( isOnMap )
                {
                    // Drawing the laser on the map was
                    // only enabled for the purpose of debugging.
                    // It might go away later:
                    lineObj.layer = maskBitScaledScenery;
                    useOrigin = mapOrigin;
                    usePointing = mapPointing;
                }
                else
                {
                    lineObj.layer =  maskTransparentFX;
                }

                float width = 0.02f;

                line.SetVertexCount(2);
                line.SetWidth( width, width );
                line.SetPosition( 0, useOrigin );
                line.SetPosition( 1, useOrigin + usePointing*( (Distance>0)?Distance:MaxDistance ) );
            }
        }
    }
}
