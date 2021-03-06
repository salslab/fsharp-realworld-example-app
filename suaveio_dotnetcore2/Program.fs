﻿open System
open Suave
open Suave.Http
open Suave.Operators
open Suave.Filters
open Suave.Json
open Suave.Writers
open System.IO
open Microsoft.Extensions.Configuration
open MongoDB.Driver
open RealWorld.Effects.Actions

let setCORSHeaders =
    addHeader  "Access-Control-Allow-Origin" "*"
    >=> addHeader "Access-Control-Allow-Headers" "content-type"

let allowCors : WebPart =
    choose [
        OPTIONS >=>
            fun context ->
                context |> (
                    setCORSHeaders
                    >=> Successful.OK "CORS approved" )
    ]

let serverConfig = 
  let randomPort = Random().Next(7000, 7999)
  { defaultConfig with bindings = [ HttpBinding.createSimple HTTP "127.0.0.1" randomPort ] }

// curried functions so we can pass the database client to the actions
let validateCredentials dbClient   = RealWorld.Auth.loginWithCredentials dbClient
let updateCurrentUser dbClient     = updateUser dbClient
let userProfile dbClient username  = getUserProfile dbClient username
let followUser dbClient username   = getFollowedProfile dbClient username
let unfollowUser dbClient username = removeFollowedProfile dbClient username
let articles dbClient              = getArticles dbClient
let articlesForFeed dbClient       = getArticlesForFeed dbClient
let favArticle slug dbClient       = favoriteArticle slug dbClient
let removeFavArticle slug dbClient = removeFavoriteCurrentUser slug dbClient
let mapJsonToArticle dbClient      = createNewArticle dbClient
let deleteArticle dbClient slug    = deleteArticleBy slug dbClient
let addComment dbClient slug       = addCommentBy slug dbClient
let getComments slug dbClient      = getCommentsBySlug slug dbClient

let app (dbClient: IMongoDatabase) =
  choose [
    allowCors
    GET >=> choose [
      path "/user" >=> getCurrentUser dbClient
      pathScan "/profile/%s" (fun username -> userProfile dbClient username)
      pathScan "/articles/%s/comments" (fun slug -> getComments slug dbClient)
      path "/articles/feed" >=> articlesForFeed dbClient
      pathScan "/articles/%s" (fun slug -> getArticlesBy slug dbClient)
      path "/articles" >=> articles dbClient
      path "/tags" >=> getTagList dbClient
    ]

    POST >=> choose [
      pathScan "/articles/%s/favorite" (fun slug -> favArticle slug dbClient)
      path "/users/login" >=> validateCredentials dbClient
      path "/users" >=> registerNewUser dbClient
      pathScan "/profiles/%s/follow" (fun username -> followUser dbClient username)
      pathScan "/articles/%s/comments" (fun slug -> addComment dbClient slug) 
      path "/articles" >=> mapJsonToArticle dbClient
    ]

    PUT >=> choose [
      path "/user" >=> updateCurrentUser dbClient
      // TODO: This should be updating the article, not adding it
      pathScan "/articles/%s" (fun slug -> request(fun req -> addArticleWithSlug req.rawForm slug dbClient))
    ]

    DELETE >=> choose [
      pathScan "/profiles/%s/follow" (fun username -> unfollowUser dbClient username)
      pathScan "/articles/%s/favorite" (fun slug -> removeFavArticle slug dbClient)
      pathScan "/articles/%s/comments/%s" (fun slugAndId -> deleteComment slugAndId dbClient)
      pathScan "/articles/%s" (fun slug -> deleteArticle dbClient slug)

      RequestErrors.NOT_FOUND "Route not found"
    ]

    path "/" >=> (Successful.OK "This will return the base page.")
  ]

[<EntryPoint>]
let main argv =
  startWebServer serverConfig (RealWorld.Effects.DB.getDBClient () |> app)
  0
