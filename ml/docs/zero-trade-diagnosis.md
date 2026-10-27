# Diagnostic des backtests à zéro trade — #273

Ce diagnostic constitue la première étape ML de #273. Il ne valide pas une
promotion de modèle ni le démarrage du cycle opérationnel complet.

## Résumé agrégé autorisé

Les deux backtests ont été reproduits avec leurs données et paramètres d’origine.
Les prédictions hors échantillon et les rapports complets correspondent aux
originaux. Aucune observation individuelle, empreinte d’entrée privée, chemin
local ou identité de run n’est publiée ici.

| Modèle | Résultat sur les 288 décisions | Explication |
|---|---|---|
| Logistic Regression | 288 HOLD, aucun BUY/SELL | Les probabilités de hausse restent entre 42,73 % et 55,98 %, entre les seuils SELL ≤ 40 % et BUY ≥ 60 %. |
| Random Forest | Aucun trade exécuté | Ses 4 BUY ont une confiance entre 61,67 % et 66,67 %, sous le minimum de 70 %. Les 26 SELL suffisamment confiants ne trouvent aucune position à vendre. |

Il n’existe donc aucun achat admissible sous la politique de ces backtests. Le
capital reste inchangé. Aucun seuil n’a été abaissé et aucune transaction n’a été
créée pour rendre le résultat non nul.

Une classe prédite `1` ne représente pas automatiquement un BUY : le générateur
de signaux applique ses seuils à `probability_up`. La confiance d’un HOLD mesure
la neutralité selon la formule existante ; elle n’est pas une probabilité de
profit. Enfin, le statut Archived ne bloque pas le backtest historique : son
moteur ne consulte pas l’activation du registre.

## Reproduire avec un catalogue privé

Depuis la racine du dépôt, dans l’environnement Python ML compatible :

```powershell
$env:PYTHONPATH = "$PWD/ml/src"
ml/.venv/Scripts/python.exe ml/scripts/diagnose_zero_trades.py --catalog-dir <catalogue-prive> --as-of-date <YYYY-MM-DD>
```

L’entrée reprend le format de `scripts/export_ai_trader_catalogs.py` :
`cleaned.csv`, `registry.json` et les deux répertoires de baseline contenant
`backtest/report.json`. Il faut exactement une version de chaque famille. La
date est une borne inclusive explicite du nettoyage, et doit conserver les
observations utilisées dans le catalogue d’origine.

L’outil recrée les estimateurs **en mémoire uniquement** avec les implémentations
existantes. Il vérifie l’association au registre (paramètres, split, métriques et
empreinte des prédictions), puis reproduit le rapport avec les seuils/configuration
enregistrés. Toute divergence dans les métadonnées, prédictions ou rapports
vérifiés provoque un échec, sans choisir implicitement une autre version,
réparer les métadonnées ou modifier les entrées. Un environnement différent peut
donc empêcher la reproduction exacte. Cette vérification établit la
cohérence avec le catalogue fourni, pas l’authenticité externe des données.

La sortie JSON contient seulement des agrégats : plages de probabilités/confiance,
comptes BUY/SELL/HOLD, filtres du moteur et nombre de trades. Les comptes de confiance
ne prouvent pas une exécution : une vente peut manquer de position et une décision
peut manquer de séance suivante. Les lignes de prédiction, chemins et empreintes
privées restent absents. Le CLI renvoie 1 et un message fixe en cas d’échec, sans
publier l’exception ; les arguments invalides renvoient 2 avec un message neutre
et `--help` reste disponible. Il ne charge aucun joblib, ne fait aucune requête de marché,
n’accède pas à la base et n’écrit aucun artefact ou catalogue.

Une sortie agrégée peut encore contenir des informations internes sur un modèle :
la revoir avant tout partage. Les entrées privées et audits par décision restent
dans leurs emplacements ignorés par Git.

## Étape ML suivante

Évaluer les features, la calibration et la généralisation avec des périodes
chronologiques distinctes pour entraînement, validation et test. Ne pas choisir
après coup des seuils sur les observations qui viennent de servir au diagnostic,
ni activer un modèle uniquement pour obtenir des trades. Une reproduction exacte
ou un score de classification ne démontre pas une stratégie rentable.
