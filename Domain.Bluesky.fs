module Bluesky
open System
open Newtonsoft.Json
open System.Net.Http
open Thoth.Json.Net
open Domain
open Slack
open Cfg
open System.Threading.Tasks
    // Namespace IDs
    module NSID =
        let root = "xrpc"
        let getLikes = $"{root}/app.bsky.feed.getLikes"
        let getPosts = $"{root}/app.bsky.feed.getPosts"
        let getPostThread = $"{root}/app.bsky.feed.getPostThread"
        let getAuthorFeed = $"{root}/app.bsky.feed.getAuthorFeed"

    type Author with
        static member fromJson (tkn:Linq.JToken) =
            {
                Id              = tkn.SelectToken("$.post.author.did").ToString()
                Name            = tkn.SelectToken("$.post.author.displayName").ToString()
                Handle          = tkn.SelectToken("$.post.author.handle").ToString()
                ProfileImageUrl = tkn.SelectToken("$.post.author.avatar").ToString()
            }

    type Reply with
        static member fromJson (tkn:Linq.JToken) =
                // at://{did-identifier}/app.bsky.feed.post/3lhswsqnokm2b
                // https://bsky.app/profile/{did-identifier}/post/3lhswsqnokm2b
            let getUrlFromAt (at:string) =
                let lastSegment = at.Split('/') |> Array.last
                $"https://bsky.app/profile/{blueskyDid}/post/{lastSegment}"
            
            {
                RootPostUrl = tkn.SelectToken("$.post.record.reply.root.uri").ToString() |> getUrlFromAt
                ReplyPostUrl = tkn.SelectToken("$.post.uri").ToString() |> getUrlFromAt
                Author = Author.fromJson tkn
                CreatedAtUtc = DateTime.Parse((tkn.SelectToken("$.post.record.createdAt").ToString()), null, Globalization.DateTimeStyles.RoundtripKind)
                Text = tkn.SelectToken("$.post.record.text").ToString()
            }

                                   // <https://google.com|this is a link>
        member x.HandleAsRootLink = $"<{x.RootPostUrl}|{x.Author.Handle}>"
        member x.NameAsRootLink   = $"<{x.RootPostUrl}|{x.Author.Name}>"
        member x.HandleAsReplyLink = $"<{x.ReplyPostUrl}|{x.Author.Handle}>"
        member x.NameAsReplyLink  = $"<{x.ReplyPostUrl}|{x.Author.Name}>"

    let publicApiBaseUrl = "https://public.api.bsky.app"
    let platformName = "Bluesky"
    
    // get author feed, scan all posts for new interactions (after 'last checked' timestamp).
    let getNewReplies (lastInvocation:DateTime) =
        // when in local dev env, stretch the lookback window
        let lastInvocation = if isRunningLocally then lastInvocation.AddDays(-5.) else lastInvocation
        
        let getReplies (thread:Linq.JToken) = thread.SelectTokens("$.thread..replies[*]")

        let fetchAuthorFeedPostIds (did:string) =
            async {
            // gets only posts_with_replies by default. add 'filter' parameter to change that.
            // this ignore cursor. yeah, IMPLEMENT CURSOR-MINDED APPROACH or we can only scan the first 100 posts!
                let requestUrl = $"{publicApiBaseUrl}/{NSID.getAuthorFeed}?actor={did}&limit=100"
                use httpClient = new HttpClient()
                let! response = httpClient.GetStringAsync(requestUrl) |> Async.AwaitTask
                let json = JsonValue.Parse(response)
                let postIds =
                // might need to include more than just 'post' entries. it seems a feed can also contain reposts and replies by actor.
                    json.SelectTokens("$.feed[*].post.uri")
                    |> Seq.map(fun jtoken -> jtoken.ToString())
                return postIds
            }
            
        let getThread (postId:string) =
            async {
                let httpClient = new HttpClient()
                let requestUrl = $"{publicApiBaseUrl}/{NSID.getPostThread}?uri={postId}&depth=1000"
                let! response = httpClient.GetStringAsync(requestUrl) |> Async.AwaitTask
                let json = JsonValue.Parse(response)
                return json
            }

        let getReplies (did:string) =
            async {
                try
                    let! postIdsResult = did |> fetchAuthorFeedPostIds
                    let! threads = postIdsResult |> Seq.map getThread |> Async.Parallel
                    return
                        threads
                        |> Seq.ofArray
                        |> Seq.collect getReplies
                        |> Seq.map Reply.fromJson
                        |> Seq.filter (fun r -> r.CreatedAtUtc >= lastInvocation)
                        |> Seq.filter (fun r -> r.Author.Id <> blueskyDid)
                        |> Seq.sortBy (fun r -> r.RootPostUrl)
                        |> fun replies -> if replies |> Seq.isEmpty then Error "No new replies found on Bluesky" else Ok replies
                with
                | :? InvalidOperationException as ex -> return Error ex.Message
                | :? HttpRequestException as ex -> return Error ex.Message
                | :? TaskCanceledException as ex -> return Error ex.Message
                | :? UriFormatException as ex -> return Error ex.Message
            }

        //let getThreads (postIds:string seq) =
        //    let httpClient = new HttpClient()
        //    let fetchThread (postId:string) =
        //        async {
        //            let requestUrl = $"{publicApiBaseUrl}/{NSID.getPostThread}?uri={postId}&depth=1000"
        //            let! response = httpClient.GetStringAsync(requestUrl) |> Async.AwaitTask
        //            let json = JsonValue.Parse(response)
        //            return json
        //        }

        //    postIds |> Seq.map fetchThread |> Async.Parallel |> Async.RunSynchronously |> List.ofArray

        // returns a flat collection of all nested replies

        let newReplies = blueskyDid |> getReplies |> Async.RunSynchronously

        newReplies

    let getSlackTableAsCodeBlock (replies: Reply seq) =
        let colNames = ["Root post"; "Handle"; "Text"]
        let colWidths =
            let cols = colNames |> Seq.map String.length
            let rows = replies |> Seq.map (fun reply -> [ reply.RootPostUrl; reply.Author.Handle; reply.Text ] |> Seq.map String.length)
            
            [cols; yield! rows]
            |> Seq.transpose
            |> Seq.map Seq.max

        let codeBlock =
            let padCell (str:string) (colWidth:int) = str.PadRight colWidth

            let walledtableEdge =
                colWidths
                |> Seq.map (fun cw -> (String.replicate cw "-"))
                |> String.concat "-|-"
                |> fun s -> $"+-{s}-+"
            
            let getWalledRow (cells:string seq) =
                Seq.map2 (fun cname cwidth -> padCell cname cwidth) cells colWidths
                |> String.concat " | "
                |> fun s -> $"| {s} |"

            /// produces codeblock, example:
            //+------------------------------------------------------------------------------|-----------------------|------------------------------------------+
            //| Root post                                                                    | Handle                | Text                                     |
            //+------------------------------------------------------------------------------|-----------------------|------------------------------------------+
            //| https://bsky.app/profile/did:plc:gtprayz574c2sc4ek27mnlfy/post/3lfx3qwwze222 | aguluman.bsky.social  | Hihi Compositional IT, lovely to have y… |
            //| https://bsky.app/profile/did:plc:gtprayz574c2sc4ek27mnlfy/post/3lgd6ru3i3z2n | mafinar.bsky.social   | I wonder if any other language has this… |
            //| https://bsky.app/profile/did:plc:gtprayz574c2sc4ek27mnlfy/post/3lgd6ru3i3z2n | borar.bsky.social     | Haskell has this with a slightly differ… |
            //| https://bsky.app/profile/did:plc:gtprayz574c2sc4ek27mnlfy/post/3lgpr4fvv6u23 | retalik.bsky.social   | Such a great article! Thanks for sharin… |
            //| https://bsky.app/profile/did:plc:gtprayz574c2sc4ek27mnlfy/post/3lgzuesei672b | lamg-dev.bsky.social  | Cool algorithm!                          |
            //| https://bsky.app/profile/did:plc:gtprayz574c2sc4ek27mnlfy/post/3lhbenqvegs2k | speakezai.bsky.social | We're #fsharp stalwarts for building an… |
            //| https://bsky.app/profile/did:plc:gtprayz574c2sc4ek27mnlfy/post/3lhswsqnokm2b | 7sharp9.bsky.social   | I've had a similar experience at more t… |
            //+------------------------------------------------------------------------------|-----------------------|------------------------------------------+

            // Bluesky link to replies. both forms work, but while the handle can change, did is robust.
            // https://bsky.app/profile/{handle}/post/3lhtdyygsjk26
            // https://bsky.app/profile/{did-identifier}/post/3lhtdyygsjk26
            
            // examples: 
            // https://bsky.app/profile/speakezai.bsky.social/post/3lhtdyygsjk26
            // https://bsky.app/profile/did:plc:igaby4nr77lndu3d3ws2muo3/post/3lhtdyygsjk26

            let allRows =
                seq {
                    yield $"New replies on {platformName}"
                    yield "```"
                    yield walledtableEdge
                    yield getWalledRow colNames
                    yield walledtableEdge
                    yield! replies |> Seq.map (fun reply -> [ reply.RootPostUrl; reply.Author.Handle; reply.Text ]) |> Seq.map getWalledRow
                    yield walledtableEdge
                    yield "```"
                }
                |> fun rows ->
                    let rowsAsOne = rows |> String.concat "\\n"
                    rowsAsOne

            allRows
        
        let cblock = codeBlock |> Block.JustText

        cblock |> string

    /// Shared function - move out of this module?
    /// Try this with "rich_text_preformatted" (quoted text) instead?
    let getSlackTableAsBlocks (replies: Reply seq) =
        
        let replyToContextBlock (reply: Reply) =
            [
                Element.Image <| Image.Create reply.Author.Name reply.Author.ProfileImageUrl
                //Element.Text <| Block.Text (TextType.Markdown MarkDownStyle.Plain, $"[{reply.Author.Handle}]({reply.RootPostUrl}) | {reply.Text |> truncText}")
                Element.Text <| Block.Text (TextType.Markdown MarkDownStyle.Plain, $"{reply.HandleAsRootLink} | {reply.TextTruncated 39}")
            ]
            |> Block.Context

        replies
        |> List.ofSeq
        |> List.map replyToContextBlock
        |> Block.Blocks
        |> string

    /// Shared function - move out of this module?
    let getTeamsTableAsCard (replies: Reply seq) =
        
        let replyToContextBlock (reply: Reply) =
            [
                Element.Image <| Image.Create reply.Author.Name reply.Author.ProfileImageUrl
                //Element.Text <| Block.Text (TextType.Markdown MarkDownStyle.Plain, $"[{reply.Author.Handle}]({reply.RootPostUrl}) | {reply.Text |> truncText}")
                Element.Text <| Block.Text (TextType.Markdown MarkDownStyle.Plain, $"{reply.HandleAsRootLink} | {reply.TextTruncated 39}")
            ]
            |> Block.Context

        replies
        |> List.ofSeq
        |> List.map replyToContextBlock
        |> Block.Blocks
        |> string
    