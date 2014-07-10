/*
 * Created by SharpDevelop.
 * User: Dunbaratu
 * Date: 7/7/2014
 * Time: 7:54 PM
 * 
 * To change this template use Tools | Options | Coding | Edit Standard Headers.
 */
using System;
using System.Collections.Generic;

namespace LaserDist
{
    /// <summary>
    /// The PQS calculating tool to go with this laser distometer.
    /// Create a new instance of this per laser dist meter.
    /// </summary>
    public class LaserPQSUtil
    {
        double lightspeed = 299792458; // in m/s
        double lastFailTime = 0.0;

        public bool RayCast( Vector3d origin, Vector3d rayVec, out string name, out double dist )
        {
            List<CelestialBody> bodies = FlightGlobals.Bodies;
            double bestHitDist = -1.0;
            string bestHitName = "<none>";
            double hitDist;
            double now = Planetarium.GetUniversalTime();
            
            // For each body in the game, find if there's a hit with its surface calculator,
            // and if there is, then keep the hit that's closest (just in case two
            // bodies are in line of sight where a ray hits both of them, we want the
            // nearer of those hits.)
            foreach( CelestialBody body in bodies )
            {
                UnityEngine.Debug.Log( "eraseme: Looking for hit with body " + body.GetName() );
                if( body.pqsController != null &&
                    body.pqsController.RayIntersection( origin, rayVec, out hitDist ) )
                {
                    UnityEngine.Debug.Log( "eraseme:   Hit found, distance = " + hitDist );
                    if( bestHitDist < 0 || bestHitDist > hitDist )
                    {
                        UnityEngine.Debug.Log( "eraseme:      Hit is better than prev." );
                        bestHitDist = hitDist;
                        bestHitName = body.GetName();
                    }
                }
            }
            name = bestHitName;
            dist = bestHitDist;
            bool hitFound = (dist > 0);
            if( hitFound )
            {
                 // A hit has to be maintained steadily, long enough to to last a full lightspeed
                 // round trip, or it doesn't really count as a hit yet:
                if( now < lastFailTime + ((dist*2)/lightspeed) )
                {
                    hitFound = false;
                }
            }
            else
            {
                lastFailTime = now;
            }
            UnityEngine.Debug.Log( "eraseme:  about to return hitfound = " + hitFound.ToString() );
            return hitFound;
        }
    }
}
