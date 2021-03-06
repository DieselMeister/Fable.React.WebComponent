﻿namespace Fable.React.WebComponent

open Fable
open Fable.AST
open Fable.AST.Fable
open Utils
open System.Runtime.InteropServices
    

#if FABLE_COMPILER    

do()

#else
// Tell Fable to scan for plugins in this assembly
[<assembly:ScanForPlugins>]
do()



    
/// <summary>Transforms a function into a React function component. Make sure the function is defined at the module level</summary>
type ReactWebComponentAttribute(exportDefault: bool) =
    inherit MemberDeclarationPluginAttribute()
    override _.FableMinimumVersion = "3.0"
    
    new() = ReactWebComponentAttribute(exportDefault=false)
    
    /// <summary>Transforms call-site into createElement calls</summary>
    override _.TransformCall(compiler, memb, expr) =
        //compiler.LogWarning (sprintf "%A" expr)
        //compiler.LogWarning (sprintf "%A" memb)
        let membArgs = memb.CurriedParameterGroups |> List.concat
        match expr with
        //| Fable.Call(callee, info, typeInfo, range) when List.length membArgs = List.length info.Args ->
        //    // F# Component()
        //    // JSX <Component />
        //    // JS createElement(Component, inputAnonymousRecord)
        //    (AstUtils.makeCall (AstUtils.makeImport "createElement" "react") [callee; info.Args.[0] ])
        | Fable.Call(callee, info, typeInfo, range) when List.length info.Args = 1 ->
            match info.Args.[0].Type with
            // JS createElement(Component, eventHandling)
            | Fable.Type.DeclaredType (_) ->
                Fable.Sequential [
                    AstUtils.makeImport "createElement" "react"
                    AstUtils.emitJs "createElement($0, theHtmlElementContainer.WebComponentEventHandling)" [ callee ]
                ]
            // JS createElement(Component, inputAnonymousRecord)
            | _ ->
                (AstUtils.makeCall (AstUtils.makeImport "createElement" "react") [callee; info.Args.[0] ])
        | Fable.Call(callee, info, typeInfo, range) when List.length info.Args = 2 ->
            
            // F# Component()
            // JSX <Component />
            // JS createElement(Component, inputAnonymousRecord)
            Fable.Sequential [
                AstUtils.makeImport "createElement" "react"
                AstUtils.emitJs "let myLittleComponent = function(arg) { return $0(theHtmlElementContainer.WebComponentEventHandling,arg); }" [callee]
                // Meh: the AST say tupled, but js has only the parms object here
                AstUtils.emitJs "createElement(myLittleComponent, tupledArg)" []
            ]
            
        | _ ->
            // return expression as is when it is not a call expression
            expr
    
    override this.Transform(compiler, file, decl) =
        match decl with
        | MemberNotFunction ->
            // Invalid attribute usage
            let errorMessage = sprintf "Expecting a function declation for %s when using [<ReactWebComponent>]" decl.Name
            compiler.LogWarning(errorMessage, ?range=decl.Body.Range)
            decl
        | MemberNotReturningReactElement ->
            // output of a React function component must be a ReactElement
            let errorMessage = sprintf "Expected function %s to return a ReactElement when using [<ReactWebComponent>]" decl.Name
            compiler.LogWarning(errorMessage, ?range=decl.Body.Range)
            decl
        | _ ->
            if (AstUtils.isCamelCase decl.Name) then
                compiler.LogWarning(sprintf "React function component '%s' is written in camelCase format. Please consider declaring it in PascalCase (i.e. '%s') to follow conventions of React applications and allow tools such as react-refresh to pick it up." decl.Name (AstUtils.capitalize decl.Name))
    

            // do not rewrite components accepting records as input
            if decl.Args.Length = 1 && AstUtils.isRecord compiler decl.Args.[0].Type then
                // check whether the record type is defined in this file
                // trigger warning if that is case
                let definedInThisFile =
                    file.Declarations
                    |> List.tryPick (fun declaration ->
                        match declaration with
                        | Declaration.ClassDeclaration classDecl ->
                            let classEntity = compiler.GetEntity(classDecl.Entity)
                            match decl.Args.[0].Type with
                            | Fable.Type.DeclaredType (entity, genericArgs) ->
                                let declaredEntity = compiler.GetEntity(entity)
                                if classEntity.IsFSharpRecord && declaredEntity.FullName = classEntity.FullName
                                then Some declaredEntity.FullName
                                else None
    
                            | _ -> None
    
                        | Declaration.ActionDeclaration action ->
                            None
                        | _ ->
                            None
                    )
    
                match definedInThisFile with
                | Some recordTypeName ->
                    let errorMsg = String.concat "" [
                        sprintf "Function component '%s' is using a record type '%s' as an input parameter. " decl.Name recordTypeName
                        "This happens to break React tooling like react-refresh and hot module reloading. "
                        "To fix this issue, consider using use an anonymous record instead or use multiple simpler values as input parameters"
                        "Future versions of [<ReactComponent>] might not emit this warning anymore, in which case you can assume that the issue if fixed. "
                        "To learn more about the issue, see https://github.com/pmmmwh/react-refresh-webpack-plugin/issues/258"
                    ]
    
                    compiler.LogWarning(errorMsg, ?range=decl.Body.Range)
    
                | None ->
                    // nothing to report
                    ignore()
                
                { decl with ExportDefault = exportDefault }
            else if decl.Args.Length = 1 then
                
                match decl.Args.[0].Type with
                | Fable.Type.Unit ->
                    // remove arguments from functions requiring unit as input
                    { decl with Args = [ ]; ExportDefault = exportDefault }
                | Fable.Type.DeclaredType(_) ->
                    // it's the eventHandlingHelper ... maybe :D
                    { decl with ExportDefault = exportDefault }
                | _ ->
                    compiler.LogWarning (sprintf "%A" decl.Args)
                    compiler.LogError "if you have only one argument on the react function, than it should be a unit or the event handling helper."
                    decl

            else if decl.Args.Length = 2  then
                //compiler.LogError (sprintf "%A" decl.Args.[0].Type)
                match decl.Args.[0].Type,decl.Args.[1].Type with
                | Fable.Type.DeclaredType({ EntityRef.FullName = fn; EntityRef.Path = _},[]), Fable.Type.AnonymousRecordType (_) ->
                    { decl with ExportDefault = exportDefault }
                | _ ->
                    compiler.LogError "ReactWebComponents only accept one anonymous record, a unit or a tuple for HTML Element which is later injected and the parms."    
                    decl
                
                //compiler.LogError (sprintf "%A" decl.Args.[0].Type)
                //{ decl with ExportDefault = exportDefault }
            else
                compiler.LogError "ReactWebComponents only accept one anonymous record, a unit or a tuple with the event dispachter and a anonymous record as parameter."
                decl






///<summary>Let Fable generate the necessary js to get a web component</summary>
///<param name="customElementName">name of the custom element</param>
///<param name="useShadowDom">use shadow dom or lite dom</param>
///<param name="style">which css file you want to inject. In case of embeddStyle=true, make sure to add the right path for the fable compiler.</param>
///<param name="embeddStyle">embedd the css code into the generated js</param>
type CreateReactWebComponentAttribute(customElementName:string, useShadowDom:bool, style:string option, embeddStyle: bool option) =
    inherit MemberDeclarationPluginAttribute()

    let transform (compiler:PluginHelper) decl typList fieldName =
        let allAreTypesStrings = typList |> List.forall (fun t -> t = Fable.String)
        if (not allAreTypesStrings) then
            compiler.LogError "For Webcomponents all properties of the anonymous record must be from type string"
            decl
        else
            let oldBody = decl.Body
            let propTypesRequiredStr =
                System.String.Join(
                    ", ",
                    fieldName 
                    |> Array.map (fun e -> sprintf "%s: PropTypes.string.isRequired" e)
                )
    
            let webCompBody =
                Fable.Sequential [
                
                    let reactFunctionWithPropsBody = 
                        AstUtils.makeCall
                            (AstUtils.makeAnonFunction
                                AstUtils.unitIdent
                                (Fable.Sequential [
                                    AstUtils.emitJs "const elem = $0" [ oldBody ]
                                            
                                    AstUtils.makeImport "PropTypes" "prop-types"
                                    AstUtils.emitJs (sprintf "elem.propTypes = { %s }" propTypesRequiredStr) []
                                    AstUtils.emitJs "elem" []
                                ])
                            )
                            []
    
    
                    let webComCall =
                        AstUtils.makeCall 
                            (AstUtils.makeImport "default" "fable-react-to-webcomponent") 
                            [ 
                                reactFunctionWithPropsBody; 
                                AstUtils.makeImport "default" "react"
                                AstUtils.makeImport "default" "react-dom"
                                AstUtils.emitJs (sprintf "{ shadow: %s %s %s }" 
                                        (if useShadowDom then "true" else "false")
                                        (match style with | None -> "" | Some style -> sprintf ", css: \"%s\"" style)
                                        (match embeddStyle with | None -> "" | Some style -> sprintf ", embeddCss: %s" (style.ToString().ToLower()))
                                    ) []

                                
                                match style, embeddStyle with
                                | _, None -> ()
                                | _, Some b when not b -> ()
                                | Some styleFile, Some b when b ->
                                    
                                    let cssFile = System.IO.File.ReadAllText(styleFile)
                                    AstUtils.makeStrConst cssFile
                                | _ ->
                                    ()
                                    
                            ]
    
    
                            
                    AstUtils.emitJs "let theHtmlElementContainer = { WebComponentEventHandling: {} }" []
                    //AstUtils.emitJs "" []
                    //AstUtils.emitJs "" []
                    AstUtils.emitJs "let myLittleWebComponent = $0" [ webComCall ]
                    //AstUtils.emitJs "let eventDispatch = myLittleWebComponent.eventHandling.dispatchEvent" []
                    //AstUtils.emitJs "let eventDispatch = function(ev) { eventDispatchImpl(ev) }" []
                    //AstUtils.emitJs "let addEventListener = myLittleWebComponent.eventHandling.addEventListener" []
                    //AstUtils.emitJs "let addEventListener = function (n, f) { addEventListenerImpl(n,f) }" []
                    //AstUtils.emitJs "let removeEventListener = myLittleWebComponent.eventHandling.removeEventListener" []
                    //AstUtils.emitJs "let removeEventListener = function (n, f) { removeEventListenerImpl(n,f) }" []
                    //AstUtils.emitJs "" []
                    AstUtils.emitJs "let webComponentEventHandling = myLittleWebComponent.eventHandling" []
                    AstUtils.emitJs "theHtmlElementContainer.WebComponentEventHandling = webComponentEventHandling" []
                    AstUtils.emitJs "customElements.define($0,myLittleWebComponent)" [ AstUtils.makeStrConst customElementName]
                ]
                
    
            let func = Fable.Lambda(AstUtils.unitIdent,webCompBody,None)
            let funcCall = AstUtils.makeCall func []
                        
                    
            {
                decl with
                    Body = funcCall
            }

    override _.FableMinimumVersion = "3.0"

    new(customElementName:string, useShadowDom:bool) 
        = CreateReactWebComponentAttribute(customElementName, useShadowDom, None, Some false)

    new(customElementName:string, style:string)
        = CreateReactWebComponentAttribute(customElementName, true, Some style, Some false)

    new(customElementName:string, style:string, embeddStyle: bool)
        = CreateReactWebComponentAttribute(customElementName, true, Some style, Some embeddStyle)

    new(customElementName:string)
        = CreateReactWebComponentAttribute(customElementName, true, None, Some false)
    
        

    override _.TransformCall(compiler, memb, expr) =
        expr
    
    override this.Transform(compiler, file, decl) =
        //compiler.LogWarning (sprintf "%A" decl.Body)
        match decl.Body with
        | Fable.Lambda(arg, body, name) ->
            //compiler.LogWarning (sprintf "%A" arg.Type)
            //compiler.LogWarning("arrived Lambda!")
            match arg.Type with
            // myReactComp (eventHandling,args)
            | Fable.Tuple [Fable.DeclaredType({FullName = injectFn},_); Fable.AnonymousRecordType (fieldName,typList)] -> // in case of a event dispatcher injection
                transform compiler decl typList fieldName
                
            // myReactComp eventHandling args (currently deactivaed because of some issues)
            //| Fable.DeclaredType (_,_) -> // in case of a event dispatcher injection
            //    match body with
            //    | Fable.Lambda (innerArg, _, _) ->
            //        match innerArg.Type with
            //        | Fable.AnonymousRecordType (fieldName,typList) ->
            //            transform compiler decl typList fieldName
            //        | _ -> 
            //            compiler.LogError ("the second argument of function must be a anonymous record, if you want to inject the eventHandling stuff.")
            //            decl
            //    | _ ->
            //        compiler.LogError ("the second argument of function must be a anonymous record, if you want to inject the eventHandling stuff.")
            //        decl
            // myReactComp args

            //myReactComp args
            | Fable.AnonymousRecordType(fieldName,typList) ->
                let allAreTypesStrings = typList |> List.forall (fun t -> t = Fable.String)
                if (not allAreTypesStrings) then
                    compiler.LogError "For Webcomponents all properties of the anonymous record must be from type string"
                    decl
                else
                    let oldBody = decl.Body
                    let propTypesRequiredStr =
                        System.String.Join(
                            ", ",
                            fieldName 
                            |> Array.map (fun e -> sprintf "%s: PropTypes.string.isRequired" e)
                        )

                    let webCompBody =
                        Fable.Sequential [
                
                            let reactFunctionWithPropsBody = 
                                AstUtils.makeCall
                                    (AstUtils.makeAnonFunction
                                        AstUtils.unitIdent
                                        (Fable.Sequential [
                                            AstUtils.emitJs "const elem = $0" [ oldBody ] 
                                            AstUtils.makeImport "PropTypes" "prop-types"
                                            AstUtils.emitJs (sprintf "elem.propTypes = { %s }" propTypesRequiredStr) []
                                            AstUtils.emitJs "elem" []
                                        ])
                                    )
                                    []


                            let webComCall =
                                AstUtils.makeCall 
                                    (AstUtils.makeImport "default" "fable-react-to-webcomponent") 
                                    [ 
                                        reactFunctionWithPropsBody; 
                                        AstUtils.makeImport "default" "react"
                                        AstUtils.makeImport "default" "react-dom"
                                        AstUtils.emitJs (sprintf "{ shadow: %s %s %s }" 
                                            (if useShadowDom then "true" else "false")
                                            (match style with | None -> "" | Some style -> sprintf ", css: \"%s\"" style)
                                            (match embeddStyle with | None -> "" | Some style -> sprintf ", embeddCss: %s" (style.ToString().ToLower()))
                                        ) []


                                        compiler.LogWarning (sprintf "%A" (style, embeddStyle))
                                        match style, embeddStyle with
                                        | _, None -> ()
                                        | _, Some b when not b -> ()
                                        | Some styleFile, Some b when b ->
                                            
                                            let cssFile = System.IO.File.ReadAllText(styleFile)
                                            AstUtils.makeStrConst cssFile
                                        | _ ->
                                            ()
                                    ]
                
                
                            AstUtils.emitJs "customElements.define($0,$1)" [ AstUtils.makeStrConst customElementName ; webComCall ]
                        ]
                
                    {
                        decl with
                            Body = webCompBody
                    }
            // myReactComp ()
            | Fable.Unit ->
                let oldBody = decl.Body
                let webCompBody =
                    Fable.Sequential [
                
                        let reactFunctionWithPropsBody = 
                            AstUtils.makeCall
                                (AstUtils.makeAnonFunction
                                    AstUtils.unitIdent
                                    (Fable.Sequential [
                                        AstUtils.emitJs "const elem = $0" [ oldBody ] 
                                        AstUtils.emitJs "elem" []
                                    ])
                                )
                                []


                        let webComCall =
                            AstUtils.makeCall 
                                (AstUtils.makeImport "default" "fable-react-to-webcomponent") 
                                [ 
                                    reactFunctionWithPropsBody; 
                                    AstUtils.makeImport "default" "react"
                                    AstUtils.makeImport "default" "react-dom"
                                    AstUtils.emitJs (sprintf "{ shadow: %s %s %s }" 
                                        (if useShadowDom then "true" else "false")
                                        (match style with | None -> "" | Some style -> sprintf ", css: \"%s\"" style)
                                        (match embeddStyle with | None -> "" | Some style -> sprintf ", embeddCss: %s" (style.ToString().ToLower()))
                                    ) []

                                    compiler.LogWarning (sprintf "%A" (style, embeddStyle))
                                    match style, embeddStyle with
                                    | _, None -> ()
                                    | _, Some b when not b -> ()
                                    | Some styleFile, Some b when b ->
                                        
                                        let cssFile = System.IO.File.ReadAllText(styleFile)
                                        AstUtils.makeStrConst cssFile
                                    | _ ->
                                        ()
                                ]
                
                
                        AstUtils.emitJs "customElements.define($0,$1)" [ AstUtils.makeStrConst customElementName ; webComCall ]
                    ]

                {
                    decl with
                        Body = webCompBody
                }
            // myReactComp eventHandling
            | Fable.DeclaredType (_) ->
                let oldBody = decl.Body
    
                let webCompBody =
                    Fable.Sequential [
                    
                        let reactFunctionWithPropsBody = 
                            AstUtils.makeCall
                                (AstUtils.makeAnonFunction
                                    AstUtils.unitIdent
                                    (Fable.Sequential [
                                        AstUtils.emitJs "const elem = $0" [ oldBody ]
                                        AstUtils.emitJs "elem" []
                                    ])
                                )
                                []
    
    
                        let webComCall =
                            AstUtils.makeCall 
                                (AstUtils.makeImport "default" "fable-react-to-webcomponent") 
                                [ 
                                    reactFunctionWithPropsBody; 
                                    AstUtils.makeImport "default" "react"
                                    AstUtils.makeImport "default" "react-dom"
                                    AstUtils.emitJs (sprintf "{ shadow: %s %s %s }" 
                                        (if useShadowDom then "true" else "false")
                                        (match style with | None -> "" | Some style -> sprintf ", css: \"%s\"" style)
                                        (match embeddStyle with | None -> "" | Some style -> sprintf ", embeddCss: %s" (style.ToString().ToLower()))
                                    ) []

                                    compiler.LogWarning (sprintf "%A" (style, embeddStyle))
                                    match style, embeddStyle with
                                    | _, None -> ()
                                    | _, Some b when not b -> ()
                                    | Some styleFile, Some b when b ->
                                        
                                        let cssFile = System.IO.File.ReadAllText(styleFile)
                                        AstUtils.makeStrConst cssFile
                                    | _ ->
                                        ()
                                ]
    
    
                                
                        AstUtils.emitJs "let theHtmlElementContainer = { WebComponentEventHandling: {} }" []
                        //AstUtils.emitJs "" []
                        //AstUtils.emitJs "" []
                        AstUtils.emitJs "let myLittleWebComponent = $0" [ webComCall ]
                        //AstUtils.emitJs "let eventDispatch = myLittleWebComponent.eventHandling.dispatchEvent" []
                        //AstUtils.emitJs "let eventDispatch = function(ev) { eventDispatchImpl(ev) }" []
                        //AstUtils.emitJs "let addEventListener = myLittleWebComponent.eventHandling.addEventListener" []
                        //AstUtils.emitJs "let addEventListener = function (n, f) { addEventListenerImpl(n,f) }" []
                        //AstUtils.emitJs "let removeEventListener = myLittleWebComponent.eventHandling.removeEventListener" []
                        //AstUtils.emitJs "let removeEventListener = function (n, f) { removeEventListenerImpl(n,f) }" []
                        //AstUtils.emitJs "" []
                        AstUtils.emitJs "let webComponentEventHandling = myLittleWebComponent.eventHandling" []
                        AstUtils.emitJs "theHtmlElementContainer.WebComponentEventHandling = webComponentEventHandling" []
                        AstUtils.emitJs "customElements.define($0,myLittleWebComponent)" [ AstUtils.makeStrConst customElementName]
                    ]
                    
    
                let func = Fable.Lambda(AstUtils.unitIdent,webCompBody,None)
                let funcCall = AstUtils.makeCall func []
                            
                        
                {
                    decl with
                        Body = funcCall
                }
            | _ ->
                //compiler.LogWarning "---------------------------- CreateReactWebComponentAttribute: Transform ------------------"
                //compiler.LogWarning (sprintf "%A" arg.Type)
                compiler.LogError "CreateReactWebComponent: the react function is not declared with an anonymous record as parameter or with the eventHandling helper or with a unit.!"    
                decl
        | _ ->
            compiler.LogError "CreateReactWebComponent: The imput for the web component must be a react element function generated from [<ReactWebComponents>]!"
            decl
#endif