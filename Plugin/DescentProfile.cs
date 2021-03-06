﻿/*
Trajectories
Copyright 2014, Youen Toupin

This file is part of Trajectories, under MIT license.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Trajectories
{
    [KSPAddon(KSPAddon.Startup.EveryScene, false)]
    class DescentProfile : MonoBehaviour
    {
        struct Node
        {
            public string name;
            public string description;
            public double angle; // in radians
            public bool horizon; // if true, angle is relative to horizon, otherwise it's relative to velocity (i.e. angle of attack)

            public double GetAngleOfAttack(Vector3d position, Vector3d velocity)
            {
                if (!horizon)
                    return angle;

                return Math.Acos(Vector3d.Dot(position, velocity) / (position.magnitude * velocity.magnitude)) - Math.PI * 0.5 + angle;
            }

            public void DoGUI()
            {
                float maxAngle = 30.0f / 180.0f * Mathf.PI;
                GUILayout.BeginHorizontal();
                GUILayout.Label(new GUIContent(name, description), GUILayout.Width(50));
                horizon = GUILayout.Toggle(horizon, new GUIContent(horizon ? "Horiz" : "AoA", "AoA = Angle of Attack = angle relatively to the velocity vector.\nHoriz = angle relatively to the horizon."), GUILayout.Width(50));
                angle = (double)GUILayout.HorizontalSlider((float)angle, -maxAngle, maxAngle, GUILayout.Width(90));
                GUILayout.Label(Math.Floor(angle * 180.0 / Math.PI).ToString() + "°", GUILayout.Width(30));
                GUILayout.EndHorizontal();
            }
        }

        private Node entry = new Node { name = "Entry", description = "Spacecraft angle when entering the atmosphere" };
        private Node highAltitude = new Node { name = "High", description = "Spacecraft angle at 50% of atmosphere height" };
        private Node lowAltitude = new Node { name = "Low", description = "Spacecraft angle at 25% of atmosphere height" };
        private Node finalApproach = new Node { name = "Ground", description = "Spacecraft angle near the ground" };

        private static DescentProfile fetch_;
        public static DescentProfile fetch { get { return fetch_; } }

        public DescentProfile()
        {
            fetch_ = this;
        }

        public void DoGUI()
        {
            entry.DoGUI();
            highAltitude.DoGUI();
            lowAltitude.DoGUI();
            finalApproach.DoGUI();
        }

        // Computes the angle of attack to follow the current profile if the aircraft is at the specified position (in world frame, but relative to the body) with the specified velocity (relative to the air, so it takes the body rotation into account)
        public double GetAngleOfAttack(CelestialBody body, Vector3d position, Vector3d velocity)
        {
            double altitude = position.magnitude - body.Radius;
            double altitudeRatio = body.atmosphere ? altitude / body.maxAtmosphereAltitude : 0;

            Node a, b;
            double aCoeff;
            
            if (altitudeRatio > 0.5)
            {
                a = entry;
                b = highAltitude;
                aCoeff = Math.Min((altitudeRatio - 0.5) * 2.0, 1.0);
            }
            else if(altitudeRatio > 0.25)
            {
                a = highAltitude;
                b = lowAltitude;
                aCoeff = altitudeRatio * 4.0 - 1.0;
            }
            else if (altitudeRatio > 0.05)
            {
                a = lowAltitude;
                b = finalApproach;
                aCoeff = altitudeRatio * 5.0 - 0.25;

                aCoeff = 1.0 - aCoeff;
                aCoeff = 1.0 - aCoeff * aCoeff;
            }
            else
            {
                return finalApproach.GetAngleOfAttack(position, velocity);
            }

            double aAoA = a.GetAngleOfAttack(position, velocity);
            double bAoA = b.GetAngleOfAttack(position, velocity);

            return aAoA * aCoeff + bAoA * (1.0 - aCoeff);
        }
    }
}
