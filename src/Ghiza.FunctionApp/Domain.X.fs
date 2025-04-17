module X
open System
open Newtonsoft.Json
open System.Net.Http
open Thoth.Json.Net
open Domain
open Slack
open Cfg
open System.Net.Http.Headers
open System.Threading.Tasks

    type Author with
        static member fromJson (tkn:Linq.JToken) =
            {
                Id              = tkn.SelectToken("$.id").ToString()
                Name            = tkn.SelectToken("$.name").ToString()
                Handle          = tkn.SelectToken("$.username").ToString()
                ProfileImageUrl = tkn.SelectToken("$.profile_image_url").ToString()
            }

    type Reply with
        static member fromJson (tkn:Linq.JToken) =
            let id             = tkn.SelectToken("$.id").ToString()
            let conversationId = tkn.SelectToken("$.conversation_id").ToString()
            let authorId       = tkn.SelectToken("$.author_id").ToString()
            let author         = tkn.SelectTokens("$.includes.users[*]") |> Seq.map Author.fromJson |> Seq.find(fun u -> u.Id = authorId)
            let createdAt      = DateTime.Parse((tkn.SelectToken("$.created_at").ToString()), null, Globalization.DateTimeStyles.RoundtripKind)

            {
                // https://x.com/ComposedPete/status/1894885767429726241
                RootPostUrl = $"https://x.com/{xHandle}/status/{conversationId}"
                ReplyPostUrl = $"https://x.com/{author.Handle}/status/{id}"
                CreatedAtUtc = createdAt
                Text = tkn.SelectToken("$.text").ToString()
                Author = author
            }

    let publicApiUrl = "https://api.twitter.com"
    //"2/tweets/search/recent?query=to:compositionalit is:reply -is:retweet -from:4842797655&tweet.fields=article,attachments,author_id,card_uri,community_id,context_annotations,conversation_id,created_at,display_text_range,edit_controls,edit_history_tweet_ids,entities,geo,id,in_reply_to_user_id,lang,media_metadata,note_tweet,referenced_tweets,reply_settings,scopes,source,text,withheld"

    // search for all tweets that are replies to @compositionalit, are not by @compositionalit and are not retweets
    let getNewReplies (lastInvocation:DateTime) =
        
        // returns a flat collection of all nested replies
        let getReplies (tweetsNode:Linq.JToken) =
            let includes = tweetsNode.SelectToken("$.includes")
            tweetsNode.SelectTokens("$.data[*]") |> Seq.map (fun tkn -> tkn.["includes"] <- includes; tkn)
        
        let fetchTweets (xHandle:string) =
            async {
                try
                    //let requestUrl = $"{publicApiUrl}/2/tweets/search/recent?query=to:compositionalit is:reply -is:retweet -from:4842797655&tweet.fields=article,attachments,author_id,card_uri,community_id,context_annotations,conversation_id,created_at,display_text_range,edit_controls,edit_history_tweet_ids,entities,geo,id,in_reply_to_user_id,lang,media_metadata,note_tweet,referenced_tweets,reply_settings,scopes,source,text,withheld"
                    let requestUrl = $"{publicApiUrl}/2/tweets/search/recent?query=to:{xHandle} is:reply -is:retweet -from:4842797655&tweet.fields=article,attachments,author_id,card_uri,community_id,context_annotations,conversation_id,created_at,display_text_range,edit_controls,edit_history_tweet_ids,entities,geo,id,in_reply_to_user_id,note_tweet,referenced_tweets,source,text&expansions=author_id&user.fields=username,profile_image_url"
                    //created_at, id, 
                    use httpClient = new HttpClient()
                    httpClient.DefaultRequestHeaders.Authorization <- AuthenticationHeaderValue("Bearer", xBearerToken)
                    let! response = httpClient.GetStringAsync(requestUrl) |> Async.AwaitTask
                    let json = JsonValue.Parse(response)
                    return
                        json
                        |> getReplies
                        |> Seq.map Reply.fromJson
                        |> Seq.filter (fun r -> r.CreatedAtUtc >= lastInvocation)
                        |> Seq.sortBy (fun r -> r.ReplyPostUrl)
                        |> fun replies -> if replies |> Seq.isEmpty then Error "No new replies found on X" else Ok replies
                with
                | :? InvalidOperationException as ex -> return Error ex.Message
                | :? HttpRequestException as ex -> return Error ex.Message
                | :? TaskCanceledException as ex -> return Error ex.Message
                | :? UriFormatException as ex -> return Error ex.Message
            }

        //let getUsers (tweetsNode:Linq.JToken) = tweetsNode.SelectTokens("$.includes.users[*]") |> List.ofSeq

        let newReplies = xHandle |> fetchTweets |> Async.RunSynchronously
        
        newReplies

    let getSlackTableAsCodeBlock (replies: Reply seq) =
        let title = "New replies on X"
        let colNames = [ "Reply post"; "Handle"; "Text" ]
        let colWidths =
            let cols = colNames |> Seq.map String.length
            let rows = replies |> Seq.map (fun reply -> [ reply.ReplyPostUrl; reply.Author.Handle; reply.Text ] |> Seq.map String.length)
            
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
                    yield title
                    yield "```"
                    yield walledtableEdge
                    yield getWalledRow colNames
                    yield walledtableEdge
                    yield! replies |> Seq.map (fun reply -> [ reply.ReplyPostUrl; reply.Author.Handle; reply.Text ]) |> Seq.map getWalledRow
                    yield walledtableEdge
                    yield "```"
                }
                |> fun rows ->
                    let rowsAsOne = rows |> String.concat "\\n"
                    rowsAsOne

            allRows
        
        let cblock = codeBlock |> Block.JustText

        cblock |> string