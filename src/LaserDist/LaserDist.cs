/*
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
        private bool debugLineDraw = false;

        /// <summary>
        /// We want to do the work once per FixedUpdate(), but NOT during the
        /// midst of the Fixeduodate() since that means half the other parts in the game
        /// have moved their objects for the next tick and half haven't yet.  That
        /// causes raycasts to become unreliable and cofusing.
        /// So instead we just flag that a FixedUpdate() has occurred, and let the next Update()
        /// just after that FixedUpdate() do the work. (but if additional Update()s happen more often
        /// than FixedUpdates, the "extra" Update()s won't do the work.)
        /// </summary>
        private bool fixedUpdateHappened = false;
        
        /// <summary>
        ///   Laser's origin relative to the part's coord transform:
        /// </summary>
        static private Vector3d relLaserOrigin;
        /// <summary>
        ///   Laser's origin in Unity World coords:
        /// </summary>
        private Vector3d origin;
        /// <summary>
        ///   Laser's pointing unit vector in Unity World coords before x/y deflection was applied.
        /// </summary>
        private Vector3d rawPointing;
        /// <summary>
        ///   Laser's pointing unit vector in Unity World coords after x/y deflection has been applied.
        /// </summary>
        private Vector3d pointing;

        /// <summary>
        ///   Laser's origin in the map view's coords:
        /// </summary>
        private Vector3d mapOrigin;
        /// <summary>
        ///   The value of `pointing`, after it has been transformed into map coords.
        /// </summary>
        private Vector3d mapPointing;
        
        /// <summary>
        /// The utility that solves raycasts for this laser.
        /// </summary>
        private LaserPQSUtil pqsTool;
        
        /// <summary>
        /// Remember the object that had been hit by bestUnityRayCastDist
        /// </summary>
        private RaycastHit bestLateUpdateHit = new RaycastHit();

        /// <summary>
        /// Track the number of Updates() there's been since having gotten a REAL hit on the
        /// object that we are currently CLAIMING is the closest hit.  Only if there's been
        /// several updates of "faking" the hit will we really change the hit to something else.
        /// This is needed to workaround the fact that Physics.Raycast only seems to occasionally
        /// hit the object and sometimes passes through it.
        /// </summary>
        private int updateForcedResultAge = 0;
        
        /// <summary>
        /// Has there been at least one FixedUpdate() within the most recent Update() in which a "real" hit
        /// has been registered?  
        /// </summary>
        private bool resetHitThisUpdate = false;

        /// <summary>
        /// When physics raycast fails to find a hit on the current best hit object, don't report it as a
        /// failure unless it happens for this number of Update()'s in a row.  Physics.Raycast is really
        /// buggy in KSP and will intermittently fail to find hits that are clearly right there:
        /// </summary>
        private static int consecutiveForcedResultsAllowed = 2;
        
        private bool doStressTest = false; // There's a debug test I left in the code that this enables.

        private bool isDrawing = false;
        private bool isOnMap = false;
        private bool isInEditor = false;
        private bool hasPower = false;
        private double prevTime = 0.0;
        private double deltaTime = 0.0;
        
        // These are settings that affect the color animation of the laser beam:
        
        private Color laserColor = new Color(1.0f,0.0f,0.0f);
        private float laserOpacityAverage = 0.45f;
        private float laserOpacityVariance = 0.20f;
        private float laserOpacityFadeMin = 0.1f; // min opacity when at max distance.
        private System.Random laserAnimationRandomizer = null;

        // This varies the "wowowow" laser thickness animation:
        private delegate float ThicknessTimeFunction(long millisec, int rand100);
        private ThicknessTimeFunction laserWidthTimeFunction = delegate(long ms, int rand100)
            {
                return 0.3f + 0.2f * (Mathf.Sin(ms/200) + (rand100/100f) - 0.5f);
            };
        private System.Diagnostics.Stopwatch thicknessWatch;


        private GameObject lineObj = null;
        private LineRenderer line = null;
        private GameObject debuglineObj = null;
        private LineRenderer debugline = null;
        private Int32 mask;
        private Int32 laserFlightDrawLayer;
        private Int32 laserMapDrawLayer;
        private Int32 laserEditorDrawLayer;


        /// <summary>Distance the laser is showing to the first collision:</summary>
        [KSPField(isPersistant=true, guiName = "Distance", guiActive = true, guiActiveEditor = true, guiUnits = "m", guiFormat = "N2")]
        public float Distance = 0.0f;

        /// <summary>Name of thing the laser is hitting:</summary>
        [KSPField(isPersistant=true, guiName = "Hit", guiActive = true, guiActiveEditor = true)]
        public string HitName = "<none>";

        [KSPField(isPersistant=true, guiName = "Layer", guiActive = true, guiActiveEditor = true)]
        public string HitLayer = "<none>"; // for debug reasons

        /// <summary>Distance the laser is checking to the first collision:</summary>
        [KSPField(isPersistant=true, guiName = "Max Sensor Range", guiActive = true, guiActiveEditor = true, guiUnits = "m")]
        public float MaxDistance = 10000f;        

        /// <summary>Distance the laser is checking to the first collision:</summary>
        [KSPField(isPersistant=true, guiName = "Max Bend X", guiActive = true, guiActiveEditor = true, guiUnits = "deg")]
        public float MaxBendX = 0f;        

        /// <summary>Distance the laser is checking to the first collision:</summary>
        [KSPField(isPersistant=true, guiName = "Max Bend Y", guiActive = true, guiActiveEditor = true, guiUnits = "deg")]
        public float MaxBendY = 0f;        

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
        
        /// <summary>How far to bend the laser beam relative to the part's "right" yaw. Negative values bend left.</summary>
        [KSPField(isPersistant=true, guiName = "Bend X", guiActive = false, guiActiveEditor = false, guiUnits = "deg", guiFormat = "N2")]
        [UI_FloatRange(minValue = -15, maxValue = 15, stepIncrement = 0.001f)]
        public float BendX = 0.0f;

        /// <summary>How far to bend the laser beam relative to the part's "up" pitch. Negative values bend down.</summary>
        [KSPField(isPersistant=true, guiName = "Bend Y", guiActive = false, guiActiveEditor = false, guiUnits = "deg", guiFormat = "N2")]
        [UI_FloatRange(minValue = -15, maxValue = 15, stepIncrement = 0.001f)]
        public float BendY = 0.0f;

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
        
        [KSPEvent(guiName = "Zero Bend", guiActive = false, guiActiveEditor = false)]
        public void ZeroBend()
        {   BendX = 0f;
            BendY = 0f;
        }

        /// <summary>
        /// Configures context menu settings that vary depending on part.cfg settings per part,
        /// and therefore can't be configured in the C# attributes syntax (which is set in stone
        /// at compile time as static data that can't change per instance).
        /// </summary>
        private void SetGuiFieldsFromSettings()
        {
            // FIXME: The logic of the code below doesn't seem to be able to support having
            // multiple different instances of this PartModule that have different max and min
            // values for the float ranges.  It seems that whichever instance edited it's max/min
            // settings most recently, it ends up applying that to ALL the other ones too.
            // As far as I can tell it's acting like all the instances share the same UI_FloatRange
            // settings, like they're static for the class or something.
            // Strangely, this only seems to be a problem in the Flight view.  In the VAB/SPH,
            // I can actually get several different ranges on the rightclick menues.  But in the
            // Flight view, They're all the same, and it's always the settings for whichever of the
            // parts happens to have been loaded onto the vessel last.
            //
            // Because of this problem, for now I'm releasing the parts with all having the same
            // deflection range until I can understand this problem.

            BaseField field;

            DebugMsg("LaserDist is trying to config GUI panel fields from settings:");
            DebugMsg(String.Format("Part name = {0}, MaxBendX = {1}, MaxBendY = {2}", part.name, MaxBendX, MaxBendY));
            
            field = Fields["BendX"];
            ((UI_FloatRange)field.uiControlEditor).minValue = -MaxBendX;
            ((UI_FloatRange)field.uiControlEditor).maxValue = MaxBendX;
            ((UI_FloatRange)field.uiControlFlight).minValue = -MaxBendX;
            ((UI_FloatRange)field.uiControlFlight).maxValue = MaxBendX;
            if ( MaxBendX == 0f )
            {   field.guiActive = false;
                field.guiActiveEditor = false;
            }
            else
            {   field.guiActive = true;
                field.guiActiveEditor = true;
            }
            
            field = Fields["BendY"];
            ((UI_FloatRange)field.uiControlEditor).minValue = -MaxBendY;
            ((UI_FloatRange)field.uiControlEditor).maxValue = MaxBendY;
            ((UI_FloatRange)field.uiControlFlight).minValue = -MaxBendY;
            ((UI_FloatRange)field.uiControlFlight).maxValue = MaxBendY;
            if ( MaxBendY == 0f )
            {   field.guiActive = false;
                field.guiActiveEditor = false;
            }
            else
            {   field.guiActive = true;
                field.guiActiveEditor = true;
            }
            
            BaseEvent evt = Events["ZeroBend"];
            if( MaxBendX == 0f && MaxBendY == 0f )
            {
                evt.guiActive = false;
                evt.guiActiveEditor = false;
            }
            else
            {   evt.guiActive = true;
                evt.guiActiveEditor = true;
            }
        }
        
        public override void OnStart(StartState state)
        {
            // Have to keep re-doing this from several hooks because
            // KSP keeps annoyingly forgetting my float range changes.
            SetGuiFieldsFromSettings();
        }
        
        /// <summary>
        /// Unity calls this hook during the activation of the partmodule on a part.
        /// </summary>
        /// <param name="state"></param>
        public override void OnAwake()
        {
            moduleName = "LaserDistModule";
            relLaserOrigin = new Vector3d(0.0,0.0,0.0);
            pqsTool = new LaserPQSUtil(part);
            pqsTool.tickPortionAllowed = (double) (CPUGreedyPercent / 100.0);
        
            SetGuiFieldsFromSettings();

            bool debugShowAllMaskNames = false; // turn on to print the following after a KSP update:
            if (debugShowAllMaskNames)
            {
                for (int i = 0; i < 32; i++)
                    System.Console.WriteLine("A layer called \"" + LayerMask.LayerToName(i) + "\" exists at bit position " + i);
            }
            // WARNING TO ANY FUTURE MAINTAINERS ABOUT THE FOLLOWING LAYERMASK SETTING:
            //
            // SQUAD does not put the layer mask values into any sort of an Enum I could find.
            // There isn't any guarantee that they'll keep the same names.  Therefore always
            // test this again after every KSP update to see if these values have
            // been changed or if more have been added.  LaserDist has been broken by
            // KSP updates in the past due to this being changed.  You can use the debug
            // printout in the lines above to see the new layer mask names after an update.
            // 
            // This is a bit-mask, but we don't have to do our own bit shifting to make it because
            // Unity provides the following string-name based way to build the mask.
            // The commented-out lines are present as a form of documentation.  It shows
            // what we're masking off - otherwise that would be unclear because those names
            // aren't mentioned elsewhere.
            mask = LayerMask.GetMask(
                "Default",    // layer number  0, which contains most physical objects that are not "scenery"
                // "TransparentFX",    // layer number  1
                // "Ignore Raycast",    // layer number  2
                // "",    // layer number  3 (no name - don't know what it is)
                "Water",    // layer number  4
                // "UI",    // layer number  5
                // "",    // layer number  6 (no name - don't know what it is)
                // "",    // layer number  7 (no name - don't know what it is)
                // "PartsList_Icons",    // layer number  8
                // "Atmosphere",    // layer number  9
                // "Scaled Scenery",    // layer number  10 (this is the map view planets, I think)
                // "UIDialog",    // layer number  11
                // "UIVectors",    // layer number  12 (i.e. lines for orbits and comm connections maybe?)
                // "UI_Mask",    // layer number  13
                // "Screens",    // layer number  14
                "Local Scenery",    // layer number  15
                // "kerbals",    // layer number  16 (presumably the hovering faces in the UI, not the 3-D in-game kerbals)
                "EVA",    // layer number  17
                // "SkySphere",    // layer number  18
                "PhysicalObjects",    // layer number  19 (don't know - maybe rocks?)
                // "Internal Space",    // layer number  20 (objects inside the cockpit in IVA view)
                // "Part Triggers",    // layer number  21 (don't know what this is)
                // "KerbalInstructors",    // layer number  22 (presumably the people's faces on screen?
                // "AeroFXIgnore",    // layer number  23 (well, it says "ignore" so I will)
                // "MapFX",    // layer number  24
                // "UIAdditional".    // layer number  25
                // "WheelCollidersIgnore",    // layer number  26
                "WheelColliders",    // layer number  27
                "TerrainColliders"    // layer number  28
                // "DragRender"    // layer number  29
                // "SurfaceFX"    // layer number  30
                // "Vectors"    // layer number  31 (UI overlay for things like lift and drag display, maybe?).
            );
            laserFlightDrawLayer = LayerMask.NameToLayer("TransparentFX");
            laserMapDrawLayer = LayerMask.NameToLayer("Scaled Scenery");
            laserEditorDrawLayer = LayerMask.NameToLayer("Default");
        }

        public override void OnActive()
        {
            GameEvents.onPartDestroyed.Add( OnLaserDestroy );
            GameEvents.onEditorShipModified.Add( OnLaserAttachDetach );
        }
        
        // Actions to control the active flag:
        // ----------------------------------------
        [KSPAction("toggle")]
        public void ActionToggle(KSPActionParam p)
        {
            Activated = ! Activated;
            if( pqsTool != null )
                pqsTool.Reset();
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
                if( pqsTool != null )
                    pqsTool.Reset();
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
            ChooseLayerBasedOnScene();

            Shader vecShader = Shader.Find("Particles/Alpha Blended"); // for when KSP version is < 1.8
            if (vecShader == null)
                vecShader = Shader.Find("Legacy Shaders/Particles/Alpha Blended"); // for when KSP version is >= 1.8

            line = lineObj.AddComponent<LineRenderer>();
            
            line.material = new Material(vecShader);
            Color c1 = laserColor;
            Color c2 = laserColor;
            line.startColor = c1;
            line.endColor = c2;
            line.enabled = true;

            laserAnimationRandomizer = new System.Random();
            bestLateUpdateHit.distance = -1f;

            if (thicknessWatch != null)
                thicknessWatch.Stop();
            thicknessWatch = new System.Diagnostics.Stopwatch();
            thicknessWatch.Start();
        }

        private void ChooseLayerBasedOnScene()
        {
            isOnMap = MapView.MapIsEnabled;
            isInEditor = HighLogic.LoadedSceneIsEditor;
            Int32 newMask; // holding in a local var temporarily for debug-ability, because Unity overrides the value
                           // if it doesn't like it when you set LineObj.layer directly, making it hard to debug
                           // what's really going on becuase there's no variable value to look at which hasn't been altered.
            if( isInEditor )
            {
                newMask = laserEditorDrawLayer;
            }
            else if( isOnMap )
            {
                // Drawing the laser on the map was
                // only enabled for the purpose of debugging.
                // It might go away later:
                newMask = laserMapDrawLayer;
            }
            else
            {
                newMask =  laserFlightDrawLayer;
            }
            lineObj.layer = newMask;
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
            if( p != this.part ) return;
            
            Activated = false;
            DrawLaser = false;
            if( pqsTool != null )
                pqsTool.Reset();
            ChangeIsDrawing();
        }
        
        public void OnDestroy()
        {
            OnLaserDestroy(this.part); // another way to catch it when a part is detached.
            GameEvents.onPartDestroyed.Remove( OnLaserDestroy );
            GameEvents.onEditorShipModified.Remove( OnLaserAttachDetach );
        }
        
        public void OnLaserAttachDetach(ShipConstruct sc)
        {
            // If this laser part isn't on the ship anymore, turn off the drawing.
            if( ! sc.Parts.Contains(this.part))
                stopDrawing();
        }
                
        
        public void FixedUpdate()
        {
            fixedUpdateHappened = true;
        }
        
        /// <summary>
        /// Recalculate the pointing vector and origin point based on part current position and bending deflections.
        /// </summary>
        private void UpdatePointing()
        {
            origin = this.part.transform.TransformPoint( relLaserOrigin );
            rawPointing = this.part.transform.rotation * Vector3d.up;
            
            if( MaxBendX > 0f || MaxBendY > 0f )
            {   // Doubles would be better than Floats here, but these come from user
                // interface rightclick menu fields that KSP demands be floats:
                Quaternion BendRotation =
                    Quaternion.AngleAxis(BendX, this.part.transform.forward) *
                    Quaternion.AngleAxis(BendY, this.part.transform.right);
                pointing = BendRotation * rawPointing;
            }
            else
            {   pointing = rawPointing;
            }
        }

        /// <summary>
        ///   Gets new distance reading if the device is on,
        ///   and handles the toggling of the display of the laser.
        /// </summary>
        public void Update()
        {
            if( ! fixedUpdateHappened )
            {
                DebugMsg("Update: a FixedUpdate hasn't happened yet, so skipping.");
                return;
            }
            DebugMsg("Update: A new FixedUpdate happened, so doing the full work this time.");
            fixedUpdateHappened = false;
            
            double nowTime = Planetarium.GetUniversalTime();
            
            pqsTool.tickPortionAllowed = (double) (CPUGreedyPercent / 100.0); // just in case user changed it in the slider.

            deltaTime = nowTime - prevTime;
            if( prevTime > 0 ) // Skips the power drain if it's the very first Update() after the scene load.
                drainPower();
            prevTime = nowTime;

            PhysicsRaycaster();
            castUpdate();
            ChangeIsDrawing();
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
                pqsTool.StressTestPQS( part.vessel.GetOrbit().referenceBody, numQueries );
                timer.Stop();
                DebugMsg( "StressTestPQS: for " + numQueries + ", " + timer.Elapsed.TotalMilliseconds + "millis" );
            }
        }

        /// <summary>
        /// Use electriccharge, and check if power is out, and if so, disable:
        /// </summary>
        private void drainPower()
        {
            if( isInEditor )
            {
                hasPower = true;
            }
            else
            {
                if( Activated )
                {
                    double drainThisUpdate = ElectricPerSecond * deltaTime;
                    double actuallyUsed = part.RequestResource( "ElectricCharge", (double)drainThisUpdate ); 
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
        }


        /// <summary>
        /// Perform Unity's Physics.RayCast() check:
        /// </summary>
        public void PhysicsRaycaster()
        {
            UpdatePointing();
            bool switchToNewHit = false;
            RaycastHit thisLateUpdateBestHit = new RaycastHit();
            
            if( hasPower && Activated && origin != null && pointing != null)
            {
                RaycastHit[] hits = null;
                hits = Physics.RaycastAll( origin, pointing, MaxDistance, mask );
                DebugMsg( "  num hits = " + hits.Length );
                if( hits.Length > 0 )
                {
                    // Get the best existing hit on THIS lateUpdate:
                    thisLateUpdateBestHit.distance = Mathf.Infinity;
                    foreach( RaycastHit hit in hits )
                    {
                        if( hit.distance < thisLateUpdateBestHit.distance )
                            thisLateUpdateBestHit = hit;
                    }
                    DebugMsg( "    thisLateUpateBestHit = " + thisLateUpdateBestHit.distance );
                    // If it's the same object as the previous best hit, or there is no previous best hit, then use it:
                    if( bestLateUpdateHit.distance < 0  ||
                        bestLateUpdateHit.collider == null ||
                        bestLateUpdateHit.collider.gameObject == null ||
                        object.ReferenceEquals( thisLateUpdateBestHit.collider.gameObject,
                                                bestLateUpdateHit.collider.gameObject ) )
                    {
                        DebugMsg( "      Resetting hit to new value because it's the same as prev best, or there was no prev best." );
                        bestLateUpdateHit = thisLateUpdateBestHit;
                        resetHitThisUpdate = true;
                    }
                    else
                    {
                        switchToNewHit = false;
                        // If it's a different object that was hit, and it was closer, then take it as the hit:
                        if( thisLateUpdateBestHit.distance < bestLateUpdateHit.distance )
                            switchToNewHit = true;
                        // If it's a different object that was hit, and it's farther, but there's been too many
                        // instances of lateupdates with forced bogus hitting old hits, then take it as the hit:
                        else if( updateForcedResultAge >= consecutiveForcedResultsAllowed )
                            switchToNewHit = true;

                        if( switchToNewHit )
                        {
                            DebugMsg( "      Resetting hit to new value even though it's a different hit." );
                            bestLateUpdateHit = thisLateUpdateBestHit;
                            resetHitThisUpdate = true;
                        }
                        else
                            DebugMsg( "      Keeping old best value because it's a different longer hit." );
                    }
                }
                else
                {
                    DebugMsg( "  Raycast no hits." );
                    if( updateForcedResultAge >= consecutiveForcedResultsAllowed )
                    {
                        DebugMsg( "    update is old enough to allow reset to nothing." );
                        bestLateUpdateHit = new RaycastHit(); // force it to count as a real miss.
                        bestLateUpdateHit.distance = -1f;
                        resetHitThisUpdate = true;
                    }
                }

                // If showing debug lines, this makes a purple line during LateUpdate
                // whenever the target changes to a new one:
                if( debugLineDraw )
                {
                    debuglineObj = new GameObject("LaserDist debug beam");
                    debuglineObj.layer = laserFlightDrawLayer;
                    debugline = debuglineObj.AddComponent<LineRenderer>();
            
                    debugline.material = new Material(Shader.Find("Particles/Additive") );
                    Color c1 = new Color(1.0f,0.0f,1.0f);
                    Color c2 = c1;
                    debugline.startColor = c1;
                    debugline.endColor = c2;
                    debugline.enabled = true;
                    debugline.startWidth = 0.01f;
                    debugline.endWidth = 0.01f;
                    debugline.SetPosition( 0, origin );
                    debugline.SetPosition( 1, origin + pointing*thisLateUpdateBestHit.distance );
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
            if( resetHitThisUpdate )
                updateForcedResultAge = 0;
            else
                ++updateForcedResultAge;
            float newDist = -1f;
            
            // The location of origin is different in LateUpdate than it is
            // in Update, so it has to be reset in both:
            UpdatePointing();
            
            HitName = "<none>";
            HitLayer = "<none>";
            if( hasPower && Activated && origin != null && pointing != null )
            {
                // the points on the map-space corresponding to these points is different:
                mapOrigin = ScaledSpace.LocalToScaledSpace( origin );
                mapPointing = pointing;

                if( bestLateUpdateHit.distance >= 0 )
                {
                    DebugMsg( "  using local raycast result." );
                    UpdateAge = updateForcedResultAge;
                    
                    RaycastHit hit = bestLateUpdateHit;
                    newDist = hit.distance;

                    // Walk up the UnityGameObject tree trying to find an object that is
                    // something the user will be familiar with:
                    GameObject hitObject = (hit.transform == null ? null : hit.transform.gameObject);
                    if( hitObject != null )
                    {
                        HitLayer = LayerMask.LayerToName(hitObject.layer); // for debug reasons

                        HitName = hitObject.name; // default if the checks below don't work.

                        // Despite the name and what the Unity documentation says,
                        // GetComponentInParent actually looks all the way up the
                        // ancestor list, not just in Parents, so these following
                        // checks are walking up the ancestors to find the one that
                        // has a KSP component assigned to it:
                        if( hitObject.layer == 15 )
                        {
                            // Support Kopernicus scatter colliders
                            // - no more crashing into boulders
                            // - shoot them with lasers! (and then drive around them)
                            PQSMod_LandClassScatterQuad scatter = hitObject.GetComponentInParent<PQSMod_LandClassScatterQuad>();
                            if( scatter != null )
                            {
                                HitName = scatter.transform.parent.name; // the name of the Scatter, eg. "Scatter boulder".
                            }
                            else
                            {
                                // Fallback to the body.
                                CelestialBody body = hitObject.GetComponentInParent<CelestialBody>();
                                if( body != null )
                                {
                                    HitName = body.name;
                                }
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
                    DebugMsg( "  numeric solver starting:." );
                    double pqsDist;
                    CelestialBody pqsBody;
                    bool success = pqsTool.RayCast( origin, pointing, out pqsBody, out pqsDist );
                    if( pqsTool.UpdateAge == 0 )
                    {
                        DebugMsg( "    UpdateAge == 0." );
                        if( success )
                        {
                            DebugMsg( "      success." );
                            // If it's a closer hit than we have already, then use it:
                            if( pqsDist < newDist || newDist < 0 )
                            {
                                HitName = pqsBody.name;
                                // Ignore any hit closer than 2km as probably bogus "vessel below PQS" hit:
                                // (it's possible for the actual terrain polygons to approximate the PQS curve
                                // in a way where the vessel sits "under" the PQS predicted altitude despite
                                // being above the polygon - that generates a bogus "hit terrain" false positive
                                // as the line goes from "under" the terrain to "above" it.  The PQS systen should
                                // not need to be queried for nearby terrain, so if there isn't a nearby real raycast
                                // hit, then don't believe it when PQS claims there is one:
                                if (pqsDist >= 2000)
                                {
                                    newDist = (float)pqsDist;
                                }
                            }
                        }
                    }
                    else
                    {
                        DebugMsg( "    UpdateAge != 0." );
                        if( pqsTool.PrevSuccess )
                        {
                            DebugMsg( "      prevsuccess." );
                            // If it's a closer hit than we have already, then use it:
                            if( pqsTool.PrevDist < newDist || newDist < 0 )
                            {
                                DebugMsg( "      prevsuccess." );
                                HitName = pqsTool.PrevBodyName;
                                // Ignore any hit closer than 2km as probably bogus "vessel below PQS" hit:
                                // (see comment above in the "if" about this.)
                                if( pqsTool.PrevDist >= 2000 )
                                {
                                    newDist = (float)pqsTool.PrevDist;
                                }
                            }
                        }
                    }
                    UpdateAge = pqsTool.UpdateAge;
                }
            }
            Distance = newDist;
            DebugMsg( "Distance = "+Distance );
            resetHitThisUpdate = false;
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
                if( isOnMap )
                {
                    useOrigin = mapOrigin;
                    usePointing = mapPointing;
                }

                float width = 0.02f;

                line.positionCount = 2;
                line.SetPosition( 0, useOrigin );
                line.SetPosition( 1, useOrigin + usePointing*( (Distance>0)?Distance:MaxDistance ) );

                // Make an animation effect where the laser's opacity varies on a sine-wave-over-time pattern:
                Color c1 = laserColor;
                Color c2 = laserColor;
                c1.a = laserOpacityAverage + laserOpacityVariance * (laserAnimationRandomizer.Next(0,100) / 100f);
                c2.a = laserOpacityFadeMin;
                line.startColor = c1;
                line.endColor = c2;
                float tempWidth = width * laserWidthTimeFunction(thicknessWatch.ElapsedMilliseconds, laserAnimationRandomizer.Next(0,100));
                line.startWidth = tempWidth;
                line.endWidth  = tempWidth;
            }
        }
        
        private void DebugMsg(string message)
        {
            if( debugMsg )
                System.Console.WriteLine(message);
        }
    }
}
