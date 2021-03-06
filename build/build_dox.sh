#!/bin/bash

BUILD_FOLDER=$TRAVIS_BUILD_DIR

DOXDIR=~/tgsdox

mkdir -p $DOXDIR

if [ "$TRAVIS_PULL_REQUEST" = false ] && [ "$TRAVIS_BRANCH" = "master" ]; then
    PUBLISH_DOX=true
else
    PUBLISH_DOX=false
fi

if [ "$PUBLISH_DOX" = true ] ; then
	GITHUB_URL="github.com/$TRAVIS_REPO_SLUG"
	echo "Cloning https://git@$GITHUB_URL..."
	git clone -b gh-pages --single-branch "https://git@$GITHUB_URL" "$DOXDIR" 2> /dev/null
	rm -r "$DOXDIR/*"
fi

VERSION=$(cat "build/Version.props" | grep -oPm1 "(?<=<TgsCoreVersion>)[^<]+")

echo -e "\nPROJECT_NUMBER = $VERSION\nINPUT = $BUILD_FOLDER\nOUTPUT_DIRECTORY = $DOXDIR\nPROJECT_LOGO = $BUILD_FOLDER/build/tgs.ico\nHAVE_DOT=YES" >> "$BUILD_FOLDER/docs/Doxyfile"

doxygen "$BUILD_FOLDER/docs/Doxyfile"

if [ "$PUBLISH_DOX" = true ] ; then
	cd $DOXDIR
	git config --global push.default simple
	git config user.name "tgstation-server"
	git config user.email "tgstation-server@tgstation13.org"
	echo '# THIS BRANCH IS AUTO GENERATED BY TRAVIS CI' > README.md

	# Need to create a .nojekyll file to allow filenames starting with an underscore
	# to be seen on the gh-pages site. Therefore creating an empty .nojekyll file.
	echo "" > .nojekyll
	git add --all
	git commit -m "Deploy code docs to GitHub Pages for Travis build $TRAVIS_BUILD_NUMBER" -m "Commit: $TRAVIS_COMMIT"
	git push -f "https://$TGS4_GH_PAGES_TOKEN@$GITHUB_URL" 2>&1 | /dev/null
	cd "$BUILD_FOLDER"
	rm -rf "$DOXDIR/.git"
fi
