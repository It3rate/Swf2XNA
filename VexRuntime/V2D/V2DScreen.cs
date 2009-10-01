﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Serialization;

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

using Box2DX.Common;
using Box2DX.Dynamics;

using DDW.Display;
using DDW.V2D.Serialization;

namespace DDW.V2D
{
    [XmlRoot]
    public class V2DScreen : Screen
    {
        public World world;

        public float worldScale = 15;
        public Dictionary<string, Body> bodyMap = new Dictionary<string, Body>();
        public List<Body> bodies = new List<Body>();
        public List<Joint> joints = new List<Joint>();
        private V2DInstance rootInstance;
        
        public V2DScreen()
        {
        }
        public V2DScreen(SymbolImport symbolImport) : base(symbolImport)
        {
        }
        public V2DScreen(V2DContent v2dContent) : base(v2dContent)
        {
        }

        private void EnsureV2dWorld()
        {
            if (SymbolImport != null)
            {
                this.v2dWorld = V2DWorld.CreateFromXml(SymbolImport.fileName);
            }
        }
        private V2DInstance FindRootInstance(V2DInstance inst, string rootName)
        {
            // look through higher level instances first
            V2DInstance result = null;

            if (inst.InstanceName == rootName)
            {
                result = inst;
            }
            else
            {
                for (int i = 0; i < inst.Definition.Instances.Count; i++)
                {
                    if (inst.Definition.Instances[i].InstanceName == rootName)
                    {
                        result = inst.Definition.Instances[i];
                        break;
                    }
                }
            }

            if (result == null)
            {
                for (int i = 0; i < inst.Definition.Instances.Count; i++)
                {
                    result = FindRootInstance(inst.Definition.Instances[i], rootName);
                    if (result != null)
                    {
                        break;
                    }
                }
            }
            return result;
        }

        public void Activate(World world)
        {
            this.world = world;
            EnsureV2dWorld();

            bodyMap.Clear();
            bodyMap.Add(V2DGame.ROOT_NAME, world.GetGroundBody());


            if (SymbolImport == null || SymbolImport.instanceName == V2DGame.ROOT_NAME)
            {
                rootInstance = v2dWorld.RootInstance;
            }
            else
            {
                rootInstance = FindRootInstance(v2dWorld.RootInstance, SymbolImport.instanceName);
            }
            rootInstance.InstanceName = V2DGame.ROOT_NAME;

            Clear();

            this.Visible = true;
            this.Width = v2dWorld.Width;
            this.Height = v2dWorld.Height;
            
            AddInstance(rootInstance, this);

            Initialize();
        }
        public void Deactivate()
        {
            this.Visible = false;
            //RemoveInstance(rootInstance);            
            //bodyMap.Clear();
            //Clear();
            //this.v2dWorld = null;
            //this.world = null;
            //rootInstance = null;
        }
                
        public virtual Body GetBodyByName(string name)
        {
            Body result = null;
            for (int i = 0; i < bodies.Count; i++)
		    {
                object o = bodies[i].GetUserData();
                if(o is DisplayObject)
                {
                    if ( ((DisplayObject)o).InstanceName == name)
                    {
                        result = bodies[i];
                        break;
                    }
                }
		    }
            return result;
        }

        public override void AddChild(DisplayObject o)
        {
            base.AddChild(o);
        }
        public override void Removed(EventArgs e)
        {
            base.Removed(e);

            //bodyMap.Clear();

            //for (int i = 0; i < joints.Count; i++)
            //{
            //    world.DestroyJoint(joints[i]);			 
            //}

            //for (int i = 0; i < bodies.Count; i++)
            //{
            //    world.DestroyBody(bodies[i]);			 
            //}
        }

        protected void AddInstance(V2DInstance inst, DisplayObjectContainer parent)
        {
            if (inst != null)
            {
                V2DDefinition def = v2dWorld.GetDefinitionByName(inst.DefinitionName);
                if (def != null)
                {
                    Texture2D texture = this.GetTexture(def.LinkageName);
                    inst.Definition = def;

                    DisplayObject sp = null;

                    if (inst.InstanceName == V2DGame.ROOT_NAME)
                    {
                        sp = this;
                    }
                    else
                    {
                        sp = SetFieldWithReflection(inst, parent, texture);

                        if (sp == null)
                        {
                            sp = new V2DSprite(texture, inst);
                        }
                        parent.AddChild(sp);
                    }


                    // instances
                    for (int i = 0; i < def.Instances.Count; i++)
                    {
                        AddInstance(def.Instances[i], (DisplayObjectContainer)sp);
                    }
                    // joints
                    for (int i = 0; i < def.Joints.Count; i++)
                    {
                        AddJoint(def.Joints[i], sp.X, sp.Y);
                    }
                }
            }
        }
        protected void RemoveInstance(V2DInstance instance)
        {
            RemoveInstanceByName(instance.InstanceName);
        }
        protected void RemoveInstanceByName(string name)
        {
            Body bd = GetBodyByName(name);
            
            if(bd != null)
            {
                bodyMap.Remove(name);

                List<Joint> relatedJoints = new List<Joint>();
                for(int j = joints.Count - 1; j >= 0; j--)
                {
                    if(joints[j].GetBody1() == bd || joints[j].GetBody2() == bd)
                    {
                        joints.RemoveAt(j);
                        relatedJoints.Add(joints[j]);
                    }
                }	 
           
                for(int j = relatedJoints.Count - 1; j >= 0; j--)
                {
                    world.DestroyJoint(relatedJoints[j]);            	
                }
                world.DestroyBody(bd);

                // todo: this isn't removing non v2d displayObjects
                object o = bd.GetUserData();
                if(o is DisplayObject)
                {
                    this.RemoveChild(((DisplayObject)o));
                }
            }

        }

        protected Joint AddJoint(V2DJoint joint, float offsetX, float offsetY)
        {
            Joint jnt = null;
            Body targ0 = this.bodyMap[joint.Body1];
            Body targ1 = this.bodyMap[joint.Body2];
            Vector2 pt0 = new Vector2(joint.X + offsetX, joint.Y + offsetY);

            string name = joint.Name;
            float scale = V2DStage.GetInstance().WorldScale;

            Vec2 anchor0 = new Vec2();
            anchor0.Set(pt0.X / scale, pt0.Y / scale);
            Vec2 anchor1 = new Vec2();

            switch (joint.Type)
            {
                case V2DJointKind.Distance:
                    Vec2 pt1 = new Vec2(joint.X2 + offsetX, joint.Y2 + offsetY);
                    anchor1.Set(pt1.X / scale, pt1.Y / scale);

                    DistanceJointDef dj = new DistanceJointDef();
                    dj.Initialize(targ0, targ1, anchor0, anchor1);
                    dj.CollideConnected = joint.CollideConnected;
                    dj.DampingRatio = joint.DampingRatio;
                    dj.FrequencyHz = joint.FrequencyHz;
                    if (joint.Length != -1)
                    {
                        dj.Length = joint.Length / scale;
                    }

                    jnt = this.world.CreateJoint(dj);
                    break;

                case V2DJointKind.Revolute:
                    float rot0 = joint.Min; //(typeof(joint["min"]) == "string") ? parseFloat(joint["min"]) / 180 * Math.PI : joint["min"];
                    float rot1 = joint.Max; //(typeof(joint["max"]) == "string") ? parseFloat(joint["max"]) / 180 * Math.PI : joint["max"];

                    RevoluteJointDef rj = new RevoluteJointDef();
                    rj.Initialize(targ0, targ1, anchor0);
                    rj.LowerAngle = rot0;
                    rj.UpperAngle = rot1;

                    rj.EnableLimit = rot0 != 0 && rot1 != 0;
                    rj.MaxMotorTorque = joint.MaxMotorTorque;
                    rj.MotorSpeed = joint.MotorSpeed;
                    rj.EnableMotor = joint.EnableMotor;

                    jnt = this.world.CreateJoint(rj);
                    break;

                case V2DJointKind.Prismatic:
                    float axisX = joint.AxisX;
                    float axisY = joint.AxisY;
                    float min = joint.Min;
                    float max = joint.Max;

                    PrismaticJointDef pj = new PrismaticJointDef();
                    Vec2 worldAxis = new Vec2();
                    worldAxis.Set(axisX, axisY);
                    pj.Initialize(targ0, targ1, anchor0, worldAxis);
                    pj.LowerTranslation = min / scale;
                    pj.UpperTranslation = max / scale;

                    pj.EnableLimit = joint.EnableLimit;
                    pj.MaxMotorForce = joint.MaxMotorTorque;
                    pj.MotorSpeed = joint.MotorSpeed;
                    pj.EnableMotor = joint.EnableMotor;

                    jnt = this.world.CreateJoint(pj);
                    break;

                case V2DJointKind.Pully:
                    Vector2 pt2 = new Vector2(joint.X2 + offsetX, joint.Y2 + offsetY);
                    anchor1.Set(pt2.X / scale, pt2.Y / scale);

                    Vec2 groundAnchor0 = new Vec2();
                    groundAnchor0.Set(joint.GroundAnchor1X / scale, joint.GroundAnchor1Y / scale);

                    Vec2 groundAnchor1 = new Vec2();
                    groundAnchor1.Set(joint.GroundAnchor2X / scale, joint.GroundAnchor2Y / scale);

                    float max0 = joint.MaxLength1;
                    float max1 = joint.MaxLength2;

                    float rat = joint.Ratio;

                    PulleyJointDef puj = new PulleyJointDef();
                    puj.Initialize(targ0, targ1, groundAnchor0, groundAnchor1, anchor0, anchor1, rat);
                    puj.MaxLength1 = (max0 + max1) / scale;
                    puj.MaxLength2 = (max0 + max1) / scale;

                    puj.CollideConnected = joint.CollideConnected;

                    jnt = this.world.CreateJoint(puj);
                    break;

                case V2DJointKind.Gear:
                    GearJointDef gj = new GearJointDef();
                    gj.Body1 = targ0;
                    gj.Body2 = targ1;
                    gj.Joint1 = GetFirstGearableJoint(targ0.GetJointList());
                    gj.Joint2 = GetFirstGearableJoint(targ1.GetJointList());
                    gj.Ratio = joint.Ratio;
                    jnt = this.world.CreateJoint(gj);
                    break;
            }

            if (jnt != null)
            {
                Dictionary<string, string> dict = new Dictionary<string, string>();
                dict["name"] = name;
                jnt.UserData = dict;
                this.joints.Add(jnt);
            }

            return jnt;
        }
        public void  RemoveJoint(Joint joint)
        {	
            if(joints.Contains(joint))
            {
                joints.Remove(joint);
                world.DestroyJoint(joint);
            }
        }
        protected void  RemoveJointByName(string name)
        {				
            for(int i = joints.Count - 1; i >= 0; i--)
            {
                if((string)joints[i].UserData == name)
                {
                    RemoveJoint(joints[i]);
                    break;    
                }
            }	  
        }
        protected Joint GetFirstGearableJoint(JointEdge je)
        {
            Joint result = je.Joint;
            while (result != null && !(result is PrismaticJoint || result is RevoluteJoint))
            {
                je = je.Next;
                result = je.Joint;
                break;
            }
            return result;
        }

        protected Regex lastDigits = new Regex(@"([a-zA-Z$_]*)([0-9]+)", RegexOptions.Compiled);
        protected DisplayObject SetFieldWithReflection(V2DInstance inst, DisplayObjectContainer parent, Texture2D texture)
        {
            DisplayObject result = null;
            Type t = parent.GetType();
            string instName = inst.InstanceName;
            int index = -1;

            Match m = lastDigits.Match(instName);
            if(m.Groups.Count > 2)
            {
                instName = m.Groups[1].Value;
                index =  int.Parse(m.Groups[2].Value);
            }

            FieldInfo fi = t.GetField(instName);
            if (fi != null)
            {
                Type ft = fi.FieldType;
                if (ft.Equals(typeof(V2DSprite)) || ft.IsSubclassOf(typeof(V2DSprite)))
                {
                    ConstructorInfo ci = ft.GetConstructor(new Type[] { typeof(Texture2D), typeof(V2DInstance) });
                    result = (V2DSprite)ci.Invoke(new object[] { texture, inst });
                    fi.SetValue(parent, result);
                }else if (ft.Equals(typeof(Sprite)) || ft.IsSubclassOf(typeof(Sprite)))
                {
                    ConstructorInfo ci = ft.GetConstructor(new Type[] { typeof(Texture2D), typeof(V2DInstance) });
                    result = (Sprite)ci.Invoke(new object[] { texture, inst  });
                    fi.SetValue(parent, result);
                }
                else if (typeof(System.Collections.ICollection).IsAssignableFrom(ft))
                {
                    Type[] genTypes = ft.GetGenericArguments();
                    if (genTypes.Length == 1) // only support single type generics (eg List<>) for now
                    {
                        Type gt = genTypes[0];
                        object o = fi.GetValue(parent);
                        if (o == null) // ensure list created
                        {
                            ConstructorInfo ci = ft.GetConstructor(new Type[] {});
                            o = ci.Invoke(new object[] {});
                        }

                        if (gt.Equals(typeof(V2DSprite)) || gt.IsSubclassOf(typeof(V2DSprite)))
                        {
                            ConstructorInfo ci = gt.GetConstructor(new Type[] { typeof(Texture2D), typeof(V2DInstance) });
                            result = (V2DSprite)ci.Invoke(new object[] { texture, inst });
                            ((ICollection<V2DSprite>)o).Add((V2DSprite)result);
                        }
                        else if (gt.Equals(typeof(Sprite)) || gt.IsSubclassOf(typeof(Sprite)))
                        {
                            ConstructorInfo ci = gt.GetConstructor(new Type[] { typeof(Texture2D), typeof(V2DInstance) });
                            result = (Sprite)ci.Invoke(new object[] { texture, inst });
                            ((ICollection<Sprite>)o).Add((Sprite)result);
                        }                        
                    }
                }
                else
                {
                    throw new ArgumentException("Not supported field type. " + ft.ToString() + " " + instName);
                }
            }

            return result;
        }
    }
}