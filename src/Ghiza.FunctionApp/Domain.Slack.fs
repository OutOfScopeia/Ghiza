module Slack
// Slack UI "domain" - ghetto implementation (illegal template state is very much representable)
    type MarkDownStyle = {
        Bold: bool
        Italics: bool
        StrikeThrough: bool
    }
    with
        static member Plain        = { Bold = false; Italics = false; StrikeThrough = false }
        member x.MakeBold          = { x with Bold = true }
        member x.makeItalics       = { x with Italics = true }
        member x.MakeStrikeThrough = { x with StrikeThrough = true }

    type TextType =
        | Plain
        | Markdown of MarkDownStyle

    type Image = {
        AltText: string
        ImageUrl: string
    }
    with
        static member Create altTxt imgUrl = { AltText = altTxt; ImageUrl = imgUrl }

    type Element =
        | Image of Image
        /// Block.Text ALLOWED ONLY!
        | Text of Block
    with
        member x.Type =
            match x with
            | Image e -> "image"
            | Text  b -> b.Type
        override x.ToString() =
            match x with
            | Image i -> $"""{{"type":"{x.Type}","image_url":"{i.ImageUrl}","alt_text":"{i.AltText}"}}"""
            | Text b -> b.ToString()
    
    and Block =
        | Text of TextType * string
        | Header of Block
        | Section of Block list
        | Blocks of Block list
        | Context of Element list
        /// No blocks, just plain text
        | JustText of string

    with
        member x.Type =
            match x with
            | Text (ttype, _) -> match ttype with Plain -> "plain_text" | Markdown _ -> "mrkdwn"
            | Header        _ -> "header"
            | Section       _ -> "section"
            | Blocks        _ -> failwith "Illegal operation"
            | Context       _ -> "context"
            | JustText      _ -> failwith "Illegal operation"

        override x.ToString() =
            let invalid = """{"message":"Invalid Block Kit construct."}""" 
            
            match x with 
            | Text (ttype, txt) ->
                let bold, italic, strike =
                    match ttype with
                    | Markdown style -> (if style.Bold then "*" else ""), (if style.Italics then "_" else ""), (if style.StrikeThrough then "~" else "")
                    | Plain -> "", "", ""

                $"""{{"type":"{x.Type}","text":"{strike}{italic}{bold}{txt}{bold}{italic}{strike}"}}"""

            | Header block when block.IsText -> $"""{{"type":"{x.Type}","text":{block}}}"""
            | Section (x::[]) when x.IsText  -> $"""{{"type":"{x.Type}","text":{(x.ToString())}}}"""
            | Section fields                 -> $"""{{"type":"{x.Type}","fields":[{(fields |> Seq.map string |> String.concat ",")}]}}"""
            | Blocks sections                -> $"""{{"blocks":[{(sections |> Seq.map string |> String.concat ",")}]}}"""
            | JustText txt                   -> $"""{{"text":"{txt}"}}"""
            | Context elements when
                elements
                |> List.filter (fun e -> e.IsText )
                |> List.forall (fun e -> match e with Element.Text b -> b.IsText | Element.Image _ -> true) ->
                    $"""{{"type":"{x.Type}","elements":[{(elements |> Seq.map string |> String.concat ",")}]}}"""
            | _ -> invalid

        //member x.Finalise = $"""{{"blocks":[{x.ToString()}]}}"""