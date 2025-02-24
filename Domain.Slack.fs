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
        override x.ToString() = match x with Plain -> "plain_text" | Markdown style -> "mrkdwn"

    type Block =
        | Text of TextType * string
        | Header of Block
        | Section of Block list
        | Blocks of Block list
        /// No blocks, just plain text
        | JustText of string

    with
        override x.ToString() =
            let invalid = """{"message":"Invalid Block Kit construct."}""" 
            
            match x with 
            | Text (ttype, txt) ->
                let bold, italic, strike =
                    match ttype with
                    | Markdown style -> (if style.Bold then "*" else ""), (if style.Italics then "_" else ""), (if style.StrikeThrough then "~" else "")
                    | Plain -> "", "", ""

                $"""{{"type":"{ttype}","text":"{strike}{italic}{bold}{txt}{bold}{italic}{strike}"}}"""

            | Header block when block.IsText -> $"""{{"type":"header","text":{block}}}"""
            | Section (x::[]) when x.IsText  -> $"""{{"type":"section","text":{(x.ToString())}}}"""
            | Section fields                 -> $"""{{"type":"section","fields":[{(fields |> Seq.map string |> String.concat ",")}]}}"""
            | Blocks sections                -> $"""{{"blocks":[{(sections |> Seq.map string |> String.concat ",")}]}}"""
            | JustText txt                   -> $"""{{"text":"{txt}"}}"""
            | _ -> invalid