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

        private bool isDrawing = false;
        
        GameObject lineObj = null;
        LineRenderer line = null;
        float maxDrawDist = 10000f;
        
        /// <summary>Distance the laser is showing to the first collision:</summary>
        [KSPField(isPersistant=true, guiName = "Distance", guiActive = true)]
        public float distance = 0.0f;

        /// <summary>Name of thing the laser is hitting:</summary>
        [KSPField(isPersistant=true, guiName = "Hit", guiActive = true)]
        public string hitName = "<none>";

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
        }

        // Actions to control the active flag:
        // ----------------------------------------
        [KSPAction("activate")]
        public void ActiveOn()
        {
            activated = true;
            ChangeIsDrawing();
        }
        [KSPAction("deactivate")]
        public void ActiveOff()
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
        public void VisibilityOn()
        {
            drawLaser = true;
            ChangeIsDrawing();
        }
        [KSPAction("deactivate visibility")]
        public void VisibilityOff()
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
                if( Physics.Raycast(origin, pointing, out hit) )
                {
                    newDist = hit.distance;
                    
                    // Walk up the UnityGameObject tree trying to find an object that is
                    // something the user will be familiar with:
                    GameObject hitObject = hit.transform.gameObject;
                    if( hitObject != null )
                    {
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
                line.SetPosition( 1, origin + pointing*( (distance>0)?distance:maxDrawDist ) );
            }
        }
    }
}
