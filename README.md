# StockLab

StockLab est une plateforme d’apprentissage des marchés boursiers fondée sur l’investissement simulé. Elle associe une interface React pour explorer les marchés et gérer un portefeuille, une API .NET pour les données de marché et un module Python indépendant pour préparer les données destinées à l’apprentissage automatique.

Le dépôt est encore en développement. Le code actuel fournit un frontend fonctionnel, une API de données de marché avec des adaptateurs simulés et externes, ainsi que les premières étapes de préparation des données ML. L’authentification, la persistance des comptes, l’exécution des ordres, la persistance des alertes et les services cloud prévus ne sont pas encore des intégrations de production. Ces limites sont précisées dans ce document afin qu’un nouveau membre puisse lancer le projet sans confondre les données de démonstration avec de vraies données de compte.

## Sommaire

- [Fonctionnement de StockLab](#fonctionnement-de-stocklab)
- [État actuel](#état-actuel)
- [Stack technique](#stack-technique)
- [Architecture](#architecture)
- [Structure du dépôt](#structure-du-dépôt)
- [Prérequis](#prérequis)
- [Installation locale](#installation-locale)
- [Configuration et secrets](#configuration-et-secrets)
- [Commandes utiles](#commandes-utiles)
- [Workflow de développement](#workflow-de-développement)
- [Sécurité](#sécurité)
- [Limites et feuille de route](#limites-et-feuille-de-route)
- [Contribution](#contribution)
- [Licence](#licence)

## Fonctionnement de StockLab

Le produit aide l’utilisateur à explorer les données de marché et à s’exercer à prendre des décisions de portefeuille sans envoyer d’ordres à un courtier.

- **Marché et détails d’une action** proposent la recherche de symboles, les cotations, les graphiques historiques OHLCV, les valeurs en mouvement, les informations sur les sociétés, les actualités et des états responsives.
- **Dashboard, Portfolio, Transactions, Watchlist, Alerts, Profile et AI Trader** présentent les parcours et les écrans prévus pour les services de compte et de trading. Les données liées au compte sont actuellement vides ou indiquées comme indisponibles lorsqu’aucun service backend n’existe encore.
- **Paper trading** est le parcours de trading simulé prévu : valider les ordres BUY et SELL, mettre à jour la trésorerie et les positions, calculer la performance du portefeuille et conserver l’historique des transactions. Le service complet d’exécution est prévu ; les contrôles du frontend restent des aperçus tant que ce service n’est pas connecté.
- **AI Trader** sépare les signaux ML, l’approbation du risque et l’exécution simulée. La couche ML produit un signal et un niveau de confiance, le Risk Manager décide s’il peut être accepté, puis le Paper Trading Engine exécute l’ordre simulé approuvé. Le module Python prépare actuellement les données historiques et les variables ; il n’exécute pas de transactions.

## État actuel

Fonctionnalités présentes dans ce dépôt :

- Frontend React/TypeScript/Vite multi-pages avec les routes Login, Register, Dashboard, Market, Stock Details, Portfolio, Transactions, Watchlist, Alerts, AI Trader, Profile et Not Found.
- API ASP.NET Core avec contrôles de santé, OpenAPI en développement, réponses d’erreur sûres, validation des requêtes, mise en cache, déduplication, limitation du débit, fournisseur local simulé de données de marché et adaptateurs Twelve Data et Alpha Vantage facultatifs.
- Module ML Python 3.12 avec ingestion historique Twelve Data, validation stricte, stockage local des données brutes et traitées, nettoyage déterministe et création de variables.
- Traductions frontend française et anglaise, mises en page responsives et tests frontend, backend et ML.

Fonctionnalités non implémentées ou non connectées :

- Authentification Login/Register réelle, autorisations, profils utilisateurs et persistance des comptes.
- Portefeuilles, transactions, watchlists, alertes et exécution persistante des ordres de paper trading.
- Entraînement des modèles AI Trader, prédictions, Risk Manager, backtesting et intégration API.
- Persistance Entity Framework Core/Azure SQL, événements Azure Service Bus et services AWS S3/SQS/Lambda.

## Stack technique

| Domaine | Technologie | État dans ce dépôt |
| --- | --- | --- |
| Frontend | React 19, TypeScript, Vite, i18next, MUI X Charts | Implémenté |
| API backend | ASP.NET Core sur .NET 10, C#, OpenAPI, xUnit | Implémenté pour les données de marché |
| Données de marché | Twelve Data, Alpha Vantage, fournisseur local simulé | Le mode simulé est utilisé par défaut ; les adaptateurs externes sont facultatifs |
| Apprentissage automatique | Python 3.12, NumPy, pandas, scikit-learn, pytest, requests | Ingestion, nettoyage et création de variables implémentés |
| Persistance relationnelle | Azure SQL et Entity Framework Core | Prévu ; aucune intégration EF Core/Azure SQL n’est encore versionnée |
| Messagerie | Azure Service Bus | Prévu |
| Traitement et stockage cloud | AWS S3, SQS, Lambda, IAM | Prévu |

Les technologies prévues sont documentées pour rendre l’architecture cible visible. Elles ne doivent pas être configurées avec des endpoints ou des identifiants inventés. Pour un développement hors ligne, utilisez le fournisseur simulé et le stockage local.

## Architecture

L’exécution actuelle comporte trois zones indépendantes et testables :

    Navigateur
      │
      ├── Frontend React multi-pages Vite (frontend/)
      │       └── Requêtes /api via le proxy Vite
      │
      └── API ASP.NET Core (backend/StockLab.Api)
              ├── Contrôleurs et DTO
              ├── Contrats applicatifs (StockLab.Application)
              └── Fournisseurs et décorateurs d’infrastructure
                      ├── Données de marché simulées (mode hors ligne par défaut)
                      ├── Cotations/recherche/historique Twelve Data (facultatif)
                      └── Enrichissement/actualités Alpha Vantage (facultatif)

La solution .NET suit une structure en couches. StockLab.Domain contient les types et règles du domaine, StockLab.Application expose les contrats et les limites des cas d’utilisation, StockLab.Infrastructure implémente les fournisseurs et les services transversaux, et StockLab.Api assemble l’injection de dépendances et les endpoints HTTP. Le domaine actuel reste volontairement réduit pendant le développement de la persistance et du trading.

Le frontend utilise Vite en mode multi-pages plutôt qu’un routeur côté client. frontend/src/navigation/routes.ts est le registre partagé des routes et le middleware Vite associe les URL lisibles aux pages HTML correspondantes. Pendant le développement et la prévisualisation, le proxy API envoie les requêtes /api vers http://localhost:5274.

## Structure du dépôt

    StockLab/
    ├── .github/                         # Modèle de pull request et automatisations GitHub
    ├── backend/
    │   ├── StockLab.sln
    │   ├── StockLab.Api/                # API HTTP, contrôleurs, middleware, DI
    │   ├── StockLab.Application/        # Interfaces, DTO et contrats applicatifs
    │   ├── StockLab.Domain/             # Primitives et règles du domaine
    │   ├── StockLab.Infrastructure/     # Fournisseurs et décorateurs de données
    │   ├── StockLab.UnitTests/          # Tests .NET de l’API et des fournisseurs
    │   ├── README.md                    # Documentation spécifique au backend
    │   └── database-schema.md           # Notes sur le modèle de données actuel et prévu
    ├── frontend/
    │   ├── src/                         # Pages React, layout partagé et clients API
    │   ├── tests/                       # Tests TypeScript et tests de routage
    │   ├── public/                      # Icônes statiques et ressources publiques
    │   ├── *.html et */index.html       # Entrées multi-pages Vite
    │   ├── package.json                 # Scripts et dépendances frontend
    │   └── README.md                    # Détails du frontend et des intégrations de marché
    ├── ml/
    │   ├── src/stocklab_ml/             # Package Python
    │   ├── tests/                       # Suite pytest hors ligne
    │   ├── data/raw/                    # Données locales ignorées après ingestion
    │   ├── data/processed/              # Données locales nettoyées ignorées
    │   ├── models/                      # Artefacts locaux de modèles ignorés
    │   └── README.md                    # Configuration ML et contrats de données
    ├── docs/                            # Notes de conception et d’ingénierie
    └── README.md                        # Vue d’ensemble du projet

## Prérequis

Installez les éléments suivants avant de commencer le développement local :

- Git.
- Node.js et npm compatibles avec l’outillage Vite ; Node.js 20 ou une version plus récente est recommandé.
- SDK .NET 10.
- Python 3.12.x, pip et le module standard venv pour le travail ML.
- PowerShell sous Windows ou un shell POSIX sous macOS/Linux.

Les environnements frontend, backend et ML sont indépendants. Python est nécessaire uniquement pour ml/, et le SDK .NET uniquement pour lancer ou tester l’API.

## Installation locale

Clonez le dépôt et travaillez depuis sa racine :

    git clone https://github.com/MarcSaad-Hadidi/StockLab.git
    cd StockLab

### Frontend

Installez les dépendances et démarrez le serveur de développement Vite :

    npm --prefix frontend install
    npm --prefix frontend run dev -- --host 127.0.0.1

Ouvrez http://localhost:5173/. La route racine redirige vers Login. Les autres routes utiles sont /market, /dashboard, /portfolio, /transactions, /watchlist, /alerts, /ai-trader et /profile.

Pour générer un build de production et le prévisualiser localement :

    npm --prefix frontend run build
    npm --prefix frontend run preview -- --host 127.0.0.1

L’ancien build Dashboard reste disponible si nécessaire :

    npm --prefix frontend run build:dashboard
    npm --prefix frontend run preview -- --config vite.dashboard.config.ts --host 127.0.0.1

### API backend

Restaurez, compilez et lancez le profil HTTP dans un second terminal :

    dotnet restore backend/StockLab.sln
    dotnet build backend/StockLab.sln --no-restore
    dotnet run --project backend/StockLab.Api/StockLab.Api.csproj --launch-profile http

L’API écoute sur http://localhost:5274. Le serveur Vite frontend relaie les requêtes /api vers cette adresse. Le profil HTTPS utilise https://localhost:7247 et peut nécessiter un certificat de développement local.

Vérifiez que l’API fonctionne :

    curl http://localhost:5274/health

En environnement Development, OpenAPI est disponible depuis l’endpoint OpenAPI généré par l’API. Les routes de marché actuelles sont :

| Méthode | Route | Fonction |
| --- | --- | --- |
| GET | /health | Contrôle de santé |
| GET | /api/stocks/{symbol}/quote | Cotation |
| GET | /api/stocks/{symbol}/history | Historique OHLCV |
| GET | /api/stocks/search | Recherche de symbole ou de société |
| GET | /api/market/movers | Valeurs en mouvement |
| GET | /api/stocks/{symbol}/fundamentals | Informations fondamentales |
| GET | /api/stocks/{symbol}/logo | Métadonnées du logo |
| GET | /api/stocks/{symbol}/earnings | Informations sur les résultats |
| GET | /api/stocks/{symbol}/news | Actualités d’une société |

### Module ML (facultatif)

Créez un environnement Python isolé uniquement lorsque vous travaillez sur ml/ :

    cd ml
    python3.12 -m venv .venv

Sous Windows PowerShell, utilisez py -3.12 -m venv .venv si nécessaire, puis activez l’environnement avec .\.venv\Scripts\Activate.ps1. Sous macOS/Linux, utilisez source .venv/bin/activate. Installez ensuite les dépendances et lancez la suite hors ligne :

    python -m pip install --upgrade pip
    python -m pip install -r requirements.txt
    python -m pytest

Consultez ml/README.md pour les contrats de données, les limites d’ingestion, les règles de stockage et la clé Twelve Data ML facultative.

## Configuration et secrets

Le dépôt peut être cloné sans identifiants. Ne versionnez jamais de vraies valeurs dans appsettings*.json, les fichiers .env du frontend, les fichiers Python, les exemples de commandes ou les tests.

### Variables frontend

| Variable | Fonction | Valeur par défaut |
| --- | --- | --- |
| VITE_STOCKLAB_API_BASE_URL | URL absolue facultative de l’API utilisée par le frontend | Vide : /api utilise le proxy Vite |

Les variables frontend sont intégrées au code du navigateur. Elles ne doivent jamais contenir de clés fournisseur ni d’autres secrets.

### Configuration backend

La configuration Development versionnée sélectionne le fournisseur hors ligne :

    {
      "MarketData": { "Provider": "Mock" }
    }

Pour les secrets locaux, préférez les user-secrets .NET depuis la racine du dépôt :

    dotnet user-secrets set "TwelveData:Keys:Website" "<TWELVE_DATA_WEBSITE_KEY>" --project backend/StockLab.Api/StockLab.Api.csproj
    dotnet user-secrets set "TwelveData:Keys:Fallback" "<TWELVE_DATA_FALLBACK_KEY>" --project backend/StockLab.Api/StockLab.Api.csproj
    dotnet user-secrets set "AlphaVantage:ApiKey" "<ALPHA_VANTAGE_KEY>" --project backend/StockLab.Api/StockLab.Api.csproj

Avec des variables d’environnement au lieu des user-secrets, .NET transforme les clés de configuration imbriquées en double soulignement : TwelveData__Keys__Website, TwelveData__ActiveWebsiteKey, AlphaVantage__ApiKey et MarketData__Provider=TwelveData. Cors__AllowedOrigins__0 contrôle l’origine frontend autorisée par l’API. Le fournisseur simulé par défaut ne demande aucune clé et doit rester utilisé pour les tests hors ligne.

### Variable ML

Le fournisseur ML Python lit uniquement TWELVE_DATA_ML_API_KEY. Définissez-la dans le shell actif ou dans un gestionnaire de secrets ; ne la placez pas dans un fichier .env versionné. Cette clé est distincte des réglages TwelveData:Keys:* du backend .NET.

### Configuration cloud prévue

Azure SQL, Entity Framework Core, Azure Service Bus, AWS S3, AWS SQS, AWS Lambda et IAM sont des intégrations prévues. Aucun secret cloud ni chaîne de connexion ne doit être placé dans ce dépôt, et le code actuel n’en a pas besoin pour lancer le frontend, le backend simulé ou les tests ML hors ligne.

## Commandes utiles

Lancez ces commandes depuis la racine du dépôt :

    # Frontend
    npm --prefix frontend run lint
    npm --prefix frontend run build
    npm --prefix frontend test

    # Backend
    dotnet build backend/StockLab.sln
    dotnet test backend/StockLab.sln

    # ML
    python -m pytest ml

La suite frontend comprend des contrôles de contrats de données et des tests de routage HTTP sur les serveurs de développement et de prévisualisation. Les tests backend utilisent xUnit et le fournisseur local simulé. Les tests ML utilisent des fixtures synthétiques et bloquent les appels réseau externes.

## Workflow de développement

1. Partez du dernier develop : git switch develop puis git pull --ff-only origin develop.
2. Créez une branche ciblée avec un préfixe comme feature/, fix/, refactor/, docs/, test/ ou chore/.
3. Gardez un seul changement logique par commit. Utilisez le format Conventional Commits du projet, en anglais et en minuscules, par exemple feat(market-data): add mock market data provider.
4. Lancez les commandes de lint, build et test concernées avant le push.
5. Ouvrez une pull request vers develop, liez l’issue avec Closes #<number>, décrivez les changements et la validation, puis ajoutez des captures pour les modifications visuelles. Le modèle partagé se trouve dans .github/pull_request_template.md.
6. Traitez les commentaires de revue, fusionnez la PR approuvée et mettez à jour votre branche develop locale avant de commencer une autre issue.

Ne poussez pas directement vers main ou develop. Gardez les changements liés à l’issue et n’ajoutez pas de refactorisation sans rapport dans une PR de fonctionnalité.

## Sécurité

- Ne versionnez pas de clés API, mots de passe, chaînes de connexion, jetons, données utilisateur, jeux de données générés ou modèles entraînés.
- Utilisez les user-secrets .NET, les variables d’environnement ou le gestionnaire de secrets de l’équipe pour les identifiants locaux.
- Gardez ml/data/raw, ml/data/processed et ml/models en local ; Git ne suit que leurs fichiers .gitkeep.
- Le fournisseur par défaut utilise des données simulées. Les fournisseurs externes ont des limites de débit et des budgets de requêtes ; utilisez-les volontairement.
- StockLab est un projet d’apprentissage en paper trading. Le code actuel n’envoie pas d’ordres réels à un courtier.
- Signalez les vulnérabilités présumées de manière privée aux responsables du projet au lieu d’ouvrir une issue publique contenant des secrets ou des détails d’exploitation.

## Limites et feuille de route

L’interface affiche volontairement des états indisponibles lorsqu’aucun service de compte n’est connecté. Les travaux suivants restent prévus :

- Authentification et autorisation reposant sur un service utilisateur.
- Persistance Azure SQL via Entity Framework Core pour les utilisateurs, portefeuilles, transactions, watchlists et alertes.
- Validation et exécution des ordres simulés, positions et historique des transactions.
- Événements Azure Service Bus pour les traitements longs ou interservices.
- Entraînement et évaluation des modèles ML, génération de signaux, Risk Manager, backtesting et API AI Trader.
- Stockage AWS et traitements asynchrones pour les jeux de données, les modèles et les tâches ML.

Ces limites sont intentionnelles. Ne remplacez pas des valeurs de compte indisponibles par des soldes ou des valeurs de marché inventés simplement pour remplir une carte.

## Contribution

Lisez le README du composant concerné avant de modifier le frontend, le backend ou le ML. Créez ou mettez à jour une issue, gardez la branche ciblée, lancez les vérifications applicables et utilisez le modèle de pull request. Pour une modification d’interface, indiquez la route vérifiée, les dimensions d’écran testées et des captures lorsqu’elles aident la revue.

Documentation utile :

- frontend/README.md
- backend/README.md
- backend/database-schema.md
- ml/README.md

## Licence

Aucun fichier de licence n’a encore été déclaré pour StockLab. Traitez le dépôt comme du code privé et demandez l’accord des responsables du projet avant toute redistribution ou réutilisation.
