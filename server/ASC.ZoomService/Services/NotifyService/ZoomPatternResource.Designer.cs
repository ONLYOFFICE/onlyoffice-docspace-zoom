﻿//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//     Runtime Version:4.0.30319.42000
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------

namespace ASC.ZoomService.Services.NotifyService {
    using System;
    
    
    /// <summary>
    ///   A strongly-typed resource class, for looking up localized strings, etc.
    /// </summary>
    // This class was auto-generated by the StronglyTypedResourceBuilder
    // class via a tool like ResGen or Visual Studio.
    // To add or remove a member, edit your .ResX file then rerun ResGen
    // with the /str option, or rebuild your VS project.
    [global::System.CodeDom.Compiler.GeneratedCodeAttribute("System.Resources.Tools.StronglyTypedResourceBuilder", "17.0.0.0")]
    [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
    [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute()]
    public class ZoomPatternResource {
        
        private static global::System.Resources.ResourceManager resourceMan;
        
        private static global::System.Globalization.CultureInfo resourceCulture;
        
        [global::System.Diagnostics.CodeAnalysis.SuppressMessageAttribute("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
        internal ZoomPatternResource() {
        }
        
        /// <summary>
        ///   Returns the cached ResourceManager instance used by this class.
        /// </summary>
        [global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Advanced)]
        public static global::System.Resources.ResourceManager ResourceManager {
            get {
                if (object.ReferenceEquals(resourceMan, null)) {
                    global::System.Resources.ResourceManager temp = new global::System.Resources.ResourceManager("ASC.ZoomService.Services.NotifyService.ZoomPatternResource", typeof(ZoomPatternResource).Assembly);
                    resourceMan = temp;
                }
                return resourceMan;
            }
        }
        
        /// <summary>
        ///   Overrides the current thread's CurrentUICulture property for all
        ///   resource lookups using this strongly typed resource class.
        /// </summary>
        [global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Advanced)]
        public static global::System.Globalization.CultureInfo Culture {
            get {
                return resourceCulture;
            }
            set {
                resourceCulture = value;
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to h1. to &lt;span style=&quot;color:#FF6F3D;&quot;&gt;ONLYOFFICE&lt;/span&gt; DocSpace!
        ///
        ///
        ///Hello, $UserName!
        ///
        ///You have just created ONLYOFFICE DocSpace, a document hub where you can boost collaboration with your team, customers, partners, and more. Its address is &quot;${__VirtualRootPath}&quot;:&quot;${__VirtualRootPath}&quot;.
        ///
        ///Your current tariff plan is STARTUP. It is absolutely free and includes:
        ///
        ///&lt;table cellspacing=&quot;0&quot; cellpadding=&quot;0&quot; background=&quot;#ffffff&quot; style=&quot;background-color: #ffffff; border: 0 none; border-collapse: collapse; borde [rest of string was truncated]&quot;;.
        /// </summary>
        public static string pattern_ZoomWelcome {
            get {
                return ResourceManager.GetString("pattern_ZoomWelcome", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to &lt;patterns&gt;
        ///	&lt;formatter type=&quot;ASC.Notify.Patterns.NVelocityPatternFormatter, ASC.Core.Common&quot; /&gt;
        ///
        ///	&lt;pattern id=&quot;ZoomWelcome&quot; sender=&quot;email.sender&quot;&gt;
        ///		&lt;subject resource=&quot;|subject_ZoomWelcome|ASC.ZoomService.Services.NotifyService.ZoomPatternResource,ASC.ZoomService&quot; /&gt;
        ///		&lt;body styler=&quot;ASC.Notify.Textile.TextileStyler,ASC.Notify.Textile&quot; resource=&quot;|pattern_ZoomWelcome|ASC.ZoomService.Services.NotifyService.ZoomPatternResource,ASC.ZoomService&quot; /&gt;
        ///	&lt;/pattern&gt;
        ///&lt;/patterns&gt;.
        /// </summary>
        public static string patterns {
            get {
                return ResourceManager.GetString("patterns", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Welcome to ONLYOFFICE DocSpace!.
        /// </summary>
        public static string subject_ZoomWelcome {
            get {
                return ResourceManager.GetString("subject_ZoomWelcome", resourceCulture);
            }
        }
    }
}
