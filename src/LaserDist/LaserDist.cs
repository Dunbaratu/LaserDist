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
        /// The utility that solves raycasts for this laser.
        /// </summary>
        private LaserPQSUtil pqsTool;

        private bool isDrawing = false;
        
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
        private int maskBitDisconnectedParts = 19;
        private int maskBitPartTriggers= 21;
        private int maskBitWheelColliders = 27;
        private int maskBitTerrainCollidres = 28;

        /// <summary>Distance the laser is showing to the first collision:</summary>
        [KSPField(isPersistant=true, guiName = "Distance", guiActive = true, guiUnits = "m")]
        public float distance = 0.0f;

        /// <summary>Name of thing the laser is hitting:</summary>
        [KSPField(isPersistant=true, guiName = "Hit", guiActive = true)]
        public string hitName = "<none>";

        /// <summary>Distance the laser is showing to the first collision:</summary>
        [KSPField(isPersistant=true, guiName = "Max Sensor Range", guiActive = true, guiUnits = "m")]
        public float maxDistance = 10000f;        

        /// <summary>Flag controlling whether or not to see the laserbeam onscreen</summary>
        [KSPField(isPersistant=true, guiName = "Laser Visibility", guiActive = true)]
        private bool drawLaser = false;

        /// <summary>Flag controlling whether or not the device is taking readings</summary>
        [KSPField(isPersistant=true, guiName = "Active", guiActive = true)]
        private bool activated = false;
        
        /// <summary>
        /// Unity calls this hook during the KSP initial startup screen:
        /// </summary>
        /// <param name="state"></param>
        public override void OnAwake()
        {
            moduleName = "LaserDistModule";
            relLaserOrigin = new Vector3d(0.0,-0.3,0.0);
            pqsTool = new LaserPQSUtil();
            
            mask =  (1 << maskBitDefault)
                    + (1 << maskBitWater)
                    + (1 << maskBitPartsList)
                    // + (1 << maskBitScaledScenery) // seems to be the map scenery and it finds hits when not on mapview.
                    + (1 << maskBitLocalScenery)
                    + (1 << maskBitKerbals)
                    + (1 << maskBitDisconnectedParts)
                    + (1 << maskBitPartTriggers)
                    + (1 << maskBitWheelColliders)
                    + (1 << maskBitTerrainCollidres) ;
        }

        // Actions to control the active flag:
        // ----------------------------------------
        [KSPAction("activate")]
        public void ActiveOn(KSPActionParam p)
        {
            activated = true;
            ChangeIsDrawing();
        }
        [KSPAction("deactivate")]
        public void ActiveOff(KSPActionParam p)
        {
            activated = false;
            ChangeIsDrawing();
        }
        [KSPEvent(guiActive=true, guiName = "Toggle", active = true)]
        public void ToggleActive()
        {
            activated = ! activated;
            ChangeIsDrawing();
        }
        [KSPAction("toggle")]
        public void ActionToggle(KSPActionParam p)
        {
            ToggleActive();
        }

        // Actions to control the visibility flag:
        // ----------------------------------------
        [KSPAction("activate visibility")]
        public void VisibilityOn(KSPActionParam p)
        {
            drawLaser = true;
            ChangeIsDrawing();
        }
        [KSPAction("deactivate visibility")]
        public void VisibilityOff(KSPActionParam p)
        {
            drawLaser = false;
            ChangeIsDrawing();
        }
        [KSPEvent(guiActive=true, guiName = "Toggle Visibility", active = true)]
        public void ToggleVisibility()
        {
            drawLaser = ! drawLaser;
            ChangeIsDrawing();
        }
        [KSPAction("toggle visibility")]
        public void ActionToggleVisibility(KSPActionParam p)
        {
            ToggleVisibility();
        }
        
        
        private void ChangeIsDrawing()
        {
            bool newVal = (activated && drawLaser);
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
            lineObj = new GameObject("laser line");
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
        ///   Gets new distance reading if the device is on,
        ///   and handles the toggling of the display of the laser.
        /// </summary>
        public override void OnUpdate()
        {
            castUpdate();
            drawUpdate();
        }
        
        /// <summary>
        ///   Recalculates the distance to a hit item, or -1f if nothing
        ///   was hit by the laser.
        /// </summary>
        /// <returns></returns>
        private void castUpdate()
        {
            float newDist = -1f;
            hitName = "<none>";
            if( activated )
            {
                origin = this.part.transform.TransformPoint( relLaserOrigin );
                pointing = this.part.transform.rotation * Vector3d.down;

                RaycastHit hit;
                if( Physics.Raycast(origin, pointing, out hit, maxDistance, mask) )
                {
                    newDist = hit.distance;
                    
                    // Walk up the UnityGameObject tree trying to find an object that is
                    // something the user will be familiar with:
                    GameObject hitObject = hit.transform.gameObject;
                    if( hitObject != null )
                    {
                        UnityEngine.Debug.Log( "Raycast Hit: layer = " + hitObject.layer );
                        hitName = hitObject.name; // default if the checks below don't work.

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
                                hitName = body.name;
                            }
                        }
                        else
                        {
                            Part part = hitObject.GetComponentInParent<Part>();
                            if( part != null )
                            {
                                hitName = part.name;
                            }
                        }
                        hitName = Convert.ToString(hitObject.layer) + ": " + hitObject; // eraseme
                    }
                }
                if( newDist < 0 )
                {
                    // Try a hit a different way - using the PQS solver:
                    string pqsName;
                    double pqsDist;
                    if( pqsTool.RayCast( origin, pointing, out pqsName, out pqsDist ) )
                    {
                        hitName = pqsName;
                        newDist = (float) pqsDist;
                    }
                }
            }
            distance = newDist;
            
        }

        /// <summary>
        ///   Draws the laser line to visually show the effect.
        ///   (Also useful for debugging).
        /// </summary>
        private void drawUpdate()
        {
            if( isDrawing )
            {
                float width = 0.02f;

                line.SetVertexCount(2);
                line.SetWidth( width, width );
                line.SetPosition( 0, origin );
                line.SetPosition( 1, origin + pointing*( (distance>0)?distance:maxDistance ) );
            }
        }
    }
}
